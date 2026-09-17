#!/usr/bin/env swift

import AppKit
import CoreGraphics
import CoreImage
import CoreText
import Foundation
import ImageIO
import UniformTypeIdentifiers

// 按 ID 批量出 A4 打印夹具：每个 ID 一份 PDF，第 1 页二维码，第 2 页 AprilTag。
//
// 与 generate_pico_qr_apriltag_fixtures.swift 的分工：那一份是 PICO 相机探针的定版夹具
// （固定 id 0/250、含 A3 横版、QR 模块矩阵写死以保证跨机器逐字节可复现），它记录的是一次
// 已归档的实测，不动。这一份面向现场铺设：ID 从 0 递增、只出 A4、QR 载荷可换格式。
//
// 标族是 tagStandard41h12，与 AprilTagDetectorCore 一致。ArUco 路线已结论为 not_feasible
// （见 docs/test-fixtures/pico-camera-fiducial-tracking/README.md），代码里没有解码路径，
// 印出来检不出。

private let pointsPerMillimeter: CGFloat = 72.0 / 25.4
private let pageSize = CGSize(width: 210.0 * pointsPerMillimeter,
                              height: 297.0 * pointsPerMillimeter)

// tagStandard41h12：图幅 total_width = 9 模块，而检测器认的四边形只有 width_at_border = 5。
// 检测框只占图幅的 5/9 —— 位姿求解吃的是后者。160 mm 图幅对应 88.9 mm 检测框。
private let tagTotalWidthModules = 9.0
private let tagWidthAtBorderModules = 5.0
private let markerSize = 160.0 * pointsPerMillimeter
private let tagDetectionSizeMillimeters = 160.0 * tagWidthAtBorderModules / tagTotalWidthModules
private let qrOuterSize = 160.0 * pointsPerMillimeter
private let qrQuietZoneModules = 4
private let codeBottom = (pageSize.height - markerSize) / 2.0
private let previewWidth = 2480
private let previewHeight = 3508

private enum SheetError: Error, CustomStringConvertible {
    case usage
    case unreadableTagImage(URL)
    case tagVerificationFailed(URL)
    case qrGeneration(String)
    case contextCreation(URL)
    case imageCreation(URL)
    case imageDestination(URL)
    case imageWrite(URL)

    var description: String {
        switch self {
        case .usage:
            return """
            Usage: generate_marker_sheets.swift <output-directory> [count|tour-ids] [qr-format]
              count      number of markers, AprilTag IDs starting at 0 (default 10)
              tour-ids   comma-separated tour IDs; AprilTag ID is the list index
              qr-format  QR payload template (default "{id}")
                         {id}   AprilTag ID (the index)
                         {tour} tour ID from the list, or the index when no list given
            """
        case .unreadableTagImage(let url):
            return "Cannot read AprilTag image: \(url.path)"
        case .tagVerificationFailed(let url):
            return "Rendered tag does not match the source image (mirrored or misplaced): \(url.path)"
        case .qrGeneration(let payload):
            return "Cannot generate QR for payload: \(payload)"
        case .contextCreation(let url):
            return "Cannot create drawing context: \(url.path)"
        case .imageCreation(let url):
            return "Cannot create preview image: \(url.path)"
        case .imageDestination(let url):
            return "Cannot create PNG destination: \(url.path)"
        case .imageWrite(let url):
            return "Cannot write PNG: \(url.path)"
        }
    }
}

private struct Sheet {
    /// AprilTag ID，同时是这份夹具在整批里的序号。
    let markerID: Int

    /// 该标位绑定的 tourID。无清单时退化为序号字符串。
    let tourID: String

    let qrPayload: String
    let sourceTag: URL
    let outputStem: String
}

// MARK: - QR

/// 把二维码编成模块矩阵（true = 黑），静区已剥掉。
///
/// 定版夹具那份把矩阵写死是为了跨机器逐字节可复现；这份要支持任意载荷，只能现编。
/// 但**不嵌位图**：编出来按 1 px/模块采回矩阵，再逐模块填矢量矩形——理由与 AprilTag 那边
/// 相同，见 drawModules。
private func encodeQRModules(payload: String) throws -> [[Bool]] {
    guard let data = payload.data(using: .utf8),
          let filter = CIFilter(name: "CIQRCodeGenerator") else {
        throw SheetError.qrGeneration(payload)
    }
    filter.setValue(data, forKey: "inputMessage")
    // M：与定版夹具一致。载荷变长时版本会自动升，模块数跟着变，绘制按矩阵尺寸走。
    filter.setValue("M", forKey: "inputCorrectionLevel")

    guard let output = filter.outputImage else {
        throw SheetError.qrGeneration(payload)
    }

    let extent = output.extent
    let width = Int(extent.width), height = Int(extent.height)
    guard width > 0, height > 0 else {
        throw SheetError.qrGeneration(payload)
    }

    var pixels = [UInt8](repeating: 0, count: width * height)
    guard let context = CGContext(data: &pixels,
                                  width: width,
                                  height: height,
                                  bitsPerComponent: 8,
                                  bytesPerRow: width,
                                  space: CGColorSpaceCreateDeviceGray(),
                                  bitmapInfo: CGImageAlphaInfo.none.rawValue),
          let cgImage = CIContext(options: nil).createCGImage(output, from: extent) else {
        throw SheetError.qrGeneration(payload)
    }
    context.draw(cgImage, in: CGRect(x: 0, y: 0, width: width, height: height))

    // CIQRCodeGenerator 自带静区，宽度未文档化。剥掉全白边再按本脚本的 4 模块重新加，
    // 否则静区宽度会随 CoreImage 的实现变动而变，印出来的实际尺寸跟着变。
    var grid = (0..<height).map { row in
        (0..<width).map { column in pixels[row * width + column] <= 127 }
    }
    while let first = grid.first, !first.contains(true) { grid.removeFirst() }
    while let last = grid.last, !last.contains(true) { grid.removeLast() }
    while grid.allSatisfy({ !($0.first ?? false) }) {
        guard !(grid.first?.isEmpty ?? true) else { break }
        for index in grid.indices { grid[index].removeFirst() }
    }
    while grid.allSatisfy({ !($0.last ?? false) }) {
        guard !(grid.first?.isEmpty ?? true) else { break }
        for index in grid.indices { grid[index].removeLast() }
    }

    guard let firstRow = grid.first,
          !grid.isEmpty,
          grid.count == firstRow.count,
          grid.allSatisfy({ $0.count == firstRow.count }) else {
        throw SheetError.qrGeneration(payload)
    }
    return grid
}

// MARK: - AprilTag

/// 把 9x9 标图读成模块矩阵，第 0 行是视觉顶部，true = 黑。
private func loadTagModules(_ url: URL) throws -> [[Bool]] {
    let image = try loadImage(url)
    let width = image.width, height = image.height
    guard width == Int(tagTotalWidthModules), height == Int(tagTotalWidthModules) else {
        throw SheetError.unreadableTagImage(url)
    }

    var pixels = [UInt8](repeating: 0, count: width * height)
    guard let context = CGContext(data: &pixels,
                                  width: width,
                                  height: height,
                                  bitsPerComponent: 8,
                                  bytesPerRow: width,
                                  space: CGColorSpaceCreateDeviceGray(),
                                  bitmapInfo: CGImageAlphaInfo.none.rawValue) else {
        throw SheetError.unreadableTagImage(url)
    }
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    return (0..<height).map { row in
        (0..<width).map { column in pixels[row * width + column] <= 127 }
    }
}

private func loadImage(_ url: URL) throws -> CGImage {
    guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        throw SheetError.unreadableTagImage(url)
    }
    return image
}

// MARK: - 绘制

/// 逐模块填矩形，**不嵌位图**。
///
/// 9x9 的位图拉到 160 mm 等效约 1.4 DPI。`interpolationQuality = .none` 只在 PDF 里写一个
/// `/Interpolate false`，阅读器和打印机 RIP 完全可以不理——一旦被平滑，糊掉的正是角点精修
/// 要吃的那条边。矢量在任何打印分辨率下边缘都精确，且没有插值的余地。
private func drawModules(_ modules: [[Bool]],
                         in rect: CGRect,
                         quietZoneModules: Int,
                         context: CGContext) {
    let moduleCount = modules.count
    let totalModules = moduleCount + quietZoneModules * 2
    let moduleSize = rect.width / CGFloat(totalModules)

    context.saveGState()
    context.setFillColor(NSColor.white.cgColor)
    context.fill(rect)
    context.setFillColor(NSColor.black.cgColor)
    for (rowIndex, row) in modules.enumerated() {
        for (columnIndex, isBlack) in row.enumerated() where isBlack {
            // 页面坐标 y 轴朝上，而模块第 0 行是视觉顶部：从 maxY 往下排。
            let moduleRect = CGRect(
                x: rect.minX + CGFloat(columnIndex + quietZoneModules) * moduleSize,
                y: rect.maxY - CGFloat(rowIndex + 1 + quietZoneModules) * moduleSize,
                width: moduleSize,
                height: moduleSize
            )
            context.fill(moduleRect)
        }
    }
    context.restoreGState()
}

private func centeredText(_ text: String,
                          topMillimeters: CGFloat,
                          fontSize: CGFloat,
                          weight: NSFont.Weight,
                          context: CGContext) {
    let font = NSFont.systemFont(ofSize: fontSize, weight: weight)
    let attributed = NSAttributedString(
        string: text,
        attributes: [.font: font, .foregroundColor: NSColor.black]
    )
    let line = CTLineCreateWithAttributedString(attributed)
    let width = CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
    context.textPosition = CGPoint(x: (pageSize.width - width) / 2.0,
                                   y: pageSize.height - topMillimeters * pointsPerMillimeter)
    CTLineDraw(line, context)
}

private func clearPage(context: CGContext) {
    context.setFillColor(NSColor.white.cgColor)
    context.fill(CGRect(origin: .zero, size: pageSize))
}

private func codeRect() -> CGRect {
    CGRect(x: (pageSize.width - markerSize) / 2.0,
           y: codeBottom,
           width: markerSize,
           height: markerSize)
}

private func drawQrSheet(_ sheet: Sheet, context: CGContext) throws {
    clearPage(context: context)
    let modules = try encodeQRModules(payload: sheet.qrPayload)
    drawModules(modules,
                in: CGRect(x: (pageSize.width - qrOuterSize) / 2.0,
                           y: codeBottom,
                           width: qrOuterSize,
                           height: qrOuterSize),
                quietZoneModules: qrQuietZoneModules,
                context: context)

    centeredText("QR / Marker \(sheet.markerID) / tour \(sheet.tourID)",
                 topMillimeters: 20, fontSize: 14, weight: .semibold, context: context)
    // 载荷原文印在纸上：现场扫出来的 rawPayload 与这一行对不上就是解析链出了问题，
    // 不必回来翻生成命令。
    centeredText("payload: \(sheet.qrPayload)",
                 topMillimeters: 30, fontSize: 11, weight: .medium, context: context)
    centeredText("QR outer size 160 mm (includes \(qrQuietZoneModules)-module quiet zone)",
                 topMillimeters: 40, fontSize: 11, weight: .regular, context: context)
    centeredText("A4 portrait / 100% Actual Size / disable Fit or Scale",
                 topMillimeters: 266, fontSize: 9, weight: .regular, context: context)
}

private func drawTagSheet(_ sheet: Sheet, context: CGContext) throws {
    let modules = try loadTagModules(sheet.sourceTag)

    clearPage(context: context)
    drawModules(modules, in: codeRect(), quietZoneModules: 0, context: context)

    centeredText("AprilTag \(sheet.markerID) / tour \(sheet.tourID)",
                 topMillimeters: 20, fontSize: 14, weight: .semibold, context: context)
    // 标号与 tourID 一起印：现场铺设时这两张纸要贴在同一个展位上，
    // 而内容侧的 aprilTagID 绑定正是这一行的对应关系。
    centeredText("tagStandard41h12 / image 160 mm / aprilTagID = \(sheet.markerID)",
                 topMillimeters: 30, fontSize: 11, weight: .medium, context: context)
    // 位姿求解吃的是检测四边形，不是图幅。把要量的那条边和标称值一并印在纸上，
    // 免得现场量错对象——这是 88.9 mm，不是 160 mm。
    centeredText(String(format:
        "MEASURE the white ring outer-to-outer square: %.1f mm nominal (5/9 of image)",
        tagDetectionSizeMillimeters),
                 topMillimeters: 40, fontSize: 11, weight: .medium, context: context)
    centeredText("A4 portrait / 100% Actual Size / disable Fit or Scale",
                 topMillimeters: 266, fontSize: 9, weight: .regular, context: context)
}

// MARK: - 输出

private func writePDF(_ sheet: Sheet, to url: URL) throws {
    var mediaBox = CGRect(origin: .zero, size: pageSize)
    guard let context = CGContext(url as CFURL, mediaBox: &mediaBox, nil) else {
        throw SheetError.contextCreation(url)
    }

    context.beginPDFPage(nil)
    try drawQrSheet(sheet, context: context)
    context.endPDFPage()
    context.beginPDFPage(nil)
    try drawTagSheet(sheet, context: context)
    context.endPDFPage()
    context.closePDF()
}

/// 预览 PNG 只画 AprilTag 那一页：它的用途是喂给 verifyRenderedTag，不是给人看的样张。
private func writeTagPreviewPNG(_ sheet: Sheet, to url: URL) throws {
    guard let context = CGContext(data: nil,
                                  width: previewWidth,
                                  height: previewHeight,
                                  bitsPerComponent: 8,
                                  bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
        throw SheetError.contextCreation(url)
    }

    context.scaleBy(x: CGFloat(previewWidth) / pageSize.width,
                    y: CGFloat(previewHeight) / pageSize.height)
    try drawTagSheet(sheet, context: context)

    guard let image = context.makeImage() else {
        throw SheetError.imageCreation(url)
    }
    guard let destination = CGImageDestinationCreateWithURL(url as CFURL,
                                                           UTType.png.identifier as CFString,
                                                           1,
                                                           nil) else {
        throw SheetError.imageDestination(url)
    }
    CGImageDestinationAddImage(destination, image, [
        kCGImagePropertyDPIWidth: 300,
        kCGImagePropertyDPIHeight: 300
    ] as CFDictionary)
    guard CGImageDestinationFinalize(destination) else {
        throw SheetError.imageWrite(url)
    }
}

// MARK: - 自检

/// 把标图按模块采成 0/1 位图，第 0 行是视觉顶部。
private func sampleModules(_ image: CGImage,
                           originMillimeters: CGPoint,
                           sideMillimeters: CGFloat,
                           pageWidthMillimeters: CGFloat) -> [String] {
    let width = image.width, height = image.height
    var pixels = [UInt8](repeating: 0, count: width * height)
    guard let context = CGContext(data: &pixels,
                                  width: width,
                                  height: height,
                                  bitsPerComponent: 8,
                                  bytesPerRow: width,
                                  space: CGColorSpaceCreateDeviceGray(),
                                  bitmapInfo: CGImageAlphaInfo.none.rawValue) else {
        return []
    }
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    let pixelsPerMillimeter = CGFloat(width) / pageWidthMillimeters
    let step = sideMillimeters * pixelsPerMillimeter / CGFloat(tagTotalWidthModules)
    let x0 = originMillimeters.x * pixelsPerMillimeter
    let y0 = originMillimeters.y * pixelsPerMillimeter

    return (0..<Int(tagTotalWidthModules)).map { row in
        var line = ""
        for column in 0..<Int(tagTotalWidthModules) {
            let x = Int(x0 + (CGFloat(column) + 0.5) * step)
            let y = Int(y0 + (CGFloat(row) + 0.5) * step)
            line += pixels[y * width + x] > 127 ? "." : "#"
        }
        return line
    }
}

/// 自检：从刚写出的预览 PNG 把标采回来，和源标图逐模块比。
///
/// 针对的是上下镜像：CG 的 y 轴朝上而位图第 0 行在顶部，少翻一次坐标系标就是镜像的。
/// 镜像不是旋转，apriltag 解不了——印出来一个都检不出，纸面上肉眼完全看不出。
/// 代价是一次白打印加一轮白测。
private func verifyRenderedTag(previewURL: URL, sourceTag: URL) throws {
    let expected = sampleModules(try loadImage(sourceTag),
                                 originMillimeters: .zero,
                                 sideMillimeters: 9.0,
                                 pageWidthMillimeters: 9.0)
    let tagOriginX = (210.0 - 160.0) / 2.0
    // 采样按视觉自顶向下，所以这里要的是标顶边到页面顶边的距离，
    // 而 codeBottom 是从页面底边量的。A4 上下对称使两者数值相同，写全式子免得靠对称蒙对。
    let tagOriginY = 297.0 - codeBottom / pointsPerMillimeter - 160.0
    let rendered = sampleModules(try loadImage(previewURL),
                                 originMillimeters: CGPoint(x: tagOriginX, y: tagOriginY),
                                 sideMillimeters: 160.0,
                                 pageWidthMillimeters: 210.0)

    guard !expected.isEmpty, rendered == expected else {
        throw SheetError.tagVerificationFailed(previewURL)
    }
}

/// 自检：把刚编的 QR 用系统检测器解回来，与原载荷比。
///
/// 针对的是静区剥离剥过头、或载荷里有非 ASCII 被编错——两者印出来都是「扫不出」，
/// 而纸面上看不出任何异常。
private func verifyQRPayload(_ sheet: Sheet) throws {
    let modules = try encodeQRModules(payload: sheet.qrPayload)
    let scale = 8
    let side = (modules.count + qrQuietZoneModules * 2) * scale
    guard let context = CGContext(data: nil,
                                  width: side,
                                  height: side,
                                  bitsPerComponent: 8,
                                  bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
        throw SheetError.qrGeneration(sheet.qrPayload)
    }
    drawModules(modules,
                in: CGRect(x: 0, y: 0, width: side, height: side),
                quietZoneModules: qrQuietZoneModules,
                context: context)
    guard let image = context.makeImage() else {
        throw SheetError.qrGeneration(sheet.qrPayload)
    }

    let detector = CIDetector(ofType: CIDetectorTypeQRCode,
                              context: nil,
                              options: [CIDetectorAccuracy: CIDetectorAccuracyHigh])
    let features = detector?.features(in: CIImage(cgImage: image)) ?? []
    let decoded = (features.compactMap { ($0 as? CIQRCodeFeature)?.messageString }).first
    guard decoded == sheet.qrPayload else {
        FileHandle.standardError.write(
            Data("  QR decoded as \(decoded ?? "<nil>"), expected \(sheet.qrPayload)\n".utf8))
        throw SheetError.qrGeneration(sheet.qrPayload)
    }
}

// MARK: - main

private func run() throws {
    let arguments = CommandLine.arguments
    guard arguments.count >= 2, arguments.count <= 4 else {
        throw SheetError.usage
    }

    let outputDirectory = URL(fileURLWithPath: arguments[1], isDirectory: true)
    let selector = arguments.count >= 3 ? arguments[2] : "10"
    let qrFormat = arguments.count >= 4 ? arguments[3] : "{id}"

    // 第 2 个参数要么是数量，要么是 tourID 清单。清单里的位置就是 AprilTag ID ——
    // 内容方要填进 IteSpaceScene.Tour.aprilTagID 的正是这个对应关系。
    let tourIDs: [String]? = Int(selector) == nil
        ? selector.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }
        : nil
    let count = tourIDs?.count ?? (Int(selector) ?? 10)

    try FileManager.default.createDirectory(at: outputDirectory,
                                            withIntermediateDirectories: true)

    // 标图随仓库走，不从命令行传：它们是 AprilRobotics/apriltag-imgs 的官方 9x9 位图。
    let tagDirectory = URL(fileURLWithPath: arguments[0])
        .deletingLastPathComponent()
        .appendingPathComponent("tags", isDirectory: true)

    var bindings: [(tourID: String, tagID: Int)] = []

    for markerID in 0..<count {
        let stem = String(format: "marker_id%02d_a4", markerID)
        let tourID = tourIDs?[markerID] ?? String(markerID)
        let sheet = Sheet(
            markerID: markerID,
            tourID: tourID,
            qrPayload: qrFormat
                .replacingOccurrences(of: "{id}", with: String(markerID))
                .replacingOccurrences(of: "{tour}", with: tourID),
            sourceTag: tagDirectory.appendingPathComponent(
                String(format: "tag41_12_%05d.png", markerID)),
            outputStem: stem
        )

        let pdfURL = outputDirectory.appendingPathComponent(stem).appendingPathExtension("pdf")
        let pngURL = outputDirectory
            .appendingPathComponent(stem + "_tag_preview_300dpi").appendingPathExtension("png")

        try writePDF(sheet, to: pdfURL)
        try writeTagPreviewPNG(sheet, to: pngURL)
        try verifyRenderedTag(previewURL: pngURL, sourceTag: sheet.sourceTag)
        try verifyQRPayload(sheet)

        print("Wrote \(pdfURL.lastPathComponent)  (p1 QR payload=\"\(sheet.qrPayload)\", p2 AprilTag id=\(markerID))")
        print("  tag verified against \(sheet.sourceTag.lastPathComponent), QR payload decoded back OK")
        bindings.append((tourID, markerID))
    }

    // 内容侧要把这张表填进 IteSpaceScene.Tour.aprilTagID，否则 AprilTag 反查对每个 tour
    // 都是 null，PICO 那条扫码激活链走不通（MarkerIdentity.TryFindByTag）。
    print("\naprilTagID bindings for the space scene description:")
    for (tourID, tagID) in bindings {
        print("  \"tourID\": \"\(tourID)\"  ->  \"aprilTagID\": \(tagID)")
    }
}

do {
    try run()
} catch {
    FileHandle.standardError.write(Data("error: \(error)\n".utf8))
    exit(1)
}
