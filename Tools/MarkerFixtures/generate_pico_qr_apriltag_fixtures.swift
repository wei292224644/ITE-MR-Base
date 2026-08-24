#!/usr/bin/env swift

import AppKit
import CoreGraphics
import CoreText
import Foundation
import ImageIO
import UniformTypeIdentifiers

private let pointsPerMillimeter: CGFloat = 72.0 / 25.4
private let pageSize = CGSize(width: 210.0 * pointsPerMillimeter,
                              height: 297.0 * pointsPerMillimeter)
private let a3LandscapeSize = CGSize(width: pageSize.width * 2.0,
                                     height: pageSize.height)
// tagStandard41h12：图幅 total_width = 9 模块，而检测器认的四边形只有 width_at_border = 5 模块。
// 检测框只占图幅的 5/9——160 mm 图幅对应 88.9 mm 检测框。位姿求解用的是后者，不是前者。
private let tagTotalWidthModules = 9.0
private let tagWidthAtBorderModules = 5.0
private let markerSize = 160.0 * pointsPerMillimeter
private let tagDetectionSizeMillimeters = 160.0 * tagWidthAtBorderModules / tagTotalWidthModules
private let qrOuterSize = 160.0 * pointsPerMillimeter
private let codeBottom = (pageSize.height - markerSize) / 2.0
private let previewPageWidth = 2480
private let previewWidth = previewPageWidth * 2
private let previewHeight = 3508

// Fixed QR Version 1 / ECC M matrices generated with Nayuki's reference
// implementation (https://github.com/nayuki/QR-Code-generator). Keeping the
// modules explicit makes fixture generation deterministic on every macOS host.
private let qrModuleRows: [String: [String]] = [
    "0": [
        "111111101111101111111",
        "100000100110101000001",
        "101110100010001011101",
        "101110101110001011101",
        "101110101010101011101",
        "100000101111001000001",
        "111111101010101111111",
        "000000001001100000000",
        "100010111111011111001",
        "001010010011100101011",
        "100100101011001111100",
        "111010001100011010100",
        "100000111100111000111",
        "000000001000111000111",
        "111111101100110000010",
        "100000100001100101000",
        "101110101011001111111",
        "101110100011100101011",
        "101110100101001111100",
        "100000100100011010110",
        "111111101100111000111",
    ],
    "250": [
        "111111100101001111111",
        "100000101100101000001",
        "101110100100101011101",
        "101110100101001011101",
        "101110101011101011101",
        "100000100101001000001",
        "111111101010101111111",
        "000000000110000000000",
        "101010100010100010010",
        "010100011101010101011",
        "100011100101011100100",
        "101101001011110111001",
        "010111101101011100110",
        "000000001010001000110",
        "111111100110100010010",
        "100000100110001000110",
        "101110101010101010101",
        "101110100111010101010",
        "101110101011011101101",
        "100000100111110111000",
        "111111101111011101101",
    ],
]

private struct Fixture {
    let markerID: String
    let kind: String
    let sourceTag: URL
    let outputStem: String
}

private enum FixtureLayout {
    case dualA4
    case a3Landscape
}

private enum FixtureError: Error, CustomStringConvertible {
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
            return "Usage: generate_pico_qr_apriltag_fixtures.swift <output-directory>"
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

private func centeredText(_ text: String,
                          topMillimeters: CGFloat,
                          fontSize: CGFloat,
                          weight: NSFont.Weight,
                          context: CGContext) {
    let font = NSFont.systemFont(ofSize: fontSize, weight: weight)
    let attributed = NSAttributedString(
        string: text,
        attributes: [
            .font: font,
            .foregroundColor: NSColor.black
        ]
    )
    let line = CTLineCreateWithAttributedString(attributed)
    let width = CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
    context.textPosition = CGPoint(x: (pageSize.width - width) / 2.0,
                                   y: pageSize.height - topMillimeters * pointsPerMillimeter)
    CTLineDraw(line, context)
}

private func drawQRCode(payload: String, in outerRect: CGRect, context: CGContext) throws {
    guard let rows = qrModuleRows[payload],
          let firstRow = rows.first,
          !rows.isEmpty,
          rows.count == firstRow.count,
          rows.allSatisfy({ $0.count == firstRow.count }) else {
        throw FixtureError.qrGeneration(payload)
    }

    let quietZoneModules = 4
    let moduleCount = rows.count
    let totalModules = moduleCount + quietZoneModules * 2
    let moduleSize = outerRect.width / CGFloat(totalModules)

    context.setFillColor(NSColor.white.cgColor)
    context.fill(outerRect)
    context.setFillColor(NSColor.black.cgColor)

    for (rowIndex, row) in rows.enumerated() {
        for (columnIndex, module) in row.enumerated() {
            guard module == "1" else { continue }

            let moduleRect = CGRect(
                x: outerRect.minX + CGFloat(columnIndex + quietZoneModules) * moduleSize,
                y: outerRect.minY + CGFloat(moduleCount - 1 - rowIndex + quietZoneModules) * moduleSize,
                width: moduleSize,
                height: moduleSize
            )
            context.fill(moduleRect)
        }
    }
}

private func loadTagImage(_ url: URL) throws -> CGImage {
    guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        throw FixtureError.unreadableTagImage(url)
    }
    return image
}

/// 把 9x9 标图读成模块矩阵，第 0 行是视觉顶部，true = 黑。
private func loadTagModules(_ url: URL) throws -> [[Bool]] {
    let image = try loadTagImage(url)
    let width = image.width, height = image.height
    guard width == Int(tagTotalWidthModules), height == Int(tagTotalWidthModules) else {
        throw FixtureError.unreadableTagImage(url)
    }

    var pixels = [UInt8](repeating: 0, count: width * height)
    guard let context = CGContext(data: &pixels,
                                  width: width,
                                  height: height,
                                  bitsPerComponent: 8,
                                  bytesPerRow: width,
                                  space: CGColorSpaceCreateDeviceGray(),
                                  bitmapInfo: CGImageAlphaInfo.none.rawValue) else {
        throw FixtureError.unreadableTagImage(url)
    }
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    return (0..<height).map { row in
        (0..<width).map { column in pixels[row * width + column] <= 127 }
    }
}

/// 逐模块填矩形，**不嵌位图**。
///
/// 9x9 的位图拉到 160 mm 等效约 1.4 DPI。`interpolationQuality = .none` 只会在 PDF 里写一个
/// `/Interpolate false`，阅读器和打印机 RIP 完全可以不理——一旦被平滑，糊掉的正是角点精修
/// 要吃的那条边。矢量在任何打印分辨率下边缘都精确，且没有插值的余地。QR 那半边本来就是
/// 这么画的（见 drawQRCode），两边保持一致。
private func drawAprilTag(_ modules: [[Bool]], context: CGContext) {
    let targetRect = CGRect(
        x: (pageSize.width - markerSize) / 2.0,
        y: codeBottom,
        width: markerSize,
        height: markerSize
    )
    let moduleSize = markerSize / CGFloat(tagTotalWidthModules)

    context.saveGState()
    context.setFillColor(NSColor.black.cgColor)
    for (rowIndex, row) in modules.enumerated() {
        for (columnIndex, isBlack) in row.enumerated() where isBlack {
            // 页面坐标 y 轴朝上，而模块第 0 行是视觉顶部：从 maxY 往下排。
            let rect = CGRect(
                x: targetRect.minX + CGFloat(columnIndex) * moduleSize,
                y: targetRect.maxY - CGFloat(rowIndex + 1) * moduleSize,
                width: moduleSize,
                height: moduleSize
            )
            context.fill(rect)
        }
    }
    context.restoreGState()
}

private func clearPage(context: CGContext) {
    context.setFillColor(NSColor.white.cgColor)
    context.fill(CGRect(origin: .zero, size: pageSize))
}

private func drawQrSheet(_ fixture: Fixture,
                         layout: FixtureLayout,
                         context: CGContext) throws {
    clearPage(context: context)
    let qrRect = CGRect(
        x: (pageSize.width - qrOuterSize) / 2.0,
        y: codeBottom,
        width: qrOuterSize,
        height: qrOuterSize
    )
    try drawQRCode(payload: fixture.markerID, in: qrRect, context: context)

    let locationName = layout == .dualA4 ? "LEFT SHEET" : "LEFT PANEL"
    let mountInstruction = layout == .dualA4
        ? "Join this RIGHT page edge to the AprilTag sheet ->"
        : "A3 single sheet / QR left of AprilTag / center distance 210 mm"
    let printInstruction = layout == .dualA4
        ? "A4 portrait / 100% Actual Size / disable Fit or Scale"
        : "A3 landscape / 100% Actual Size / disable Fit or Scale"

    centeredText("\(locationName) / QR / MarkerID \(fixture.markerID)",
                 topMillimeters: 20,
                 fontSize: 14,
                 weight: .semibold,
                 context: context)
    centeredText("QR outer size 160 mm (includes 4-module quiet zone)",
                 topMillimeters: 30,
                 fontSize: 11,
                 weight: .medium,
                 context: context)
    centeredText(mountInstruction,
                 topMillimeters: 252,
                 fontSize: 9,
                 weight: .regular,
                 context: context)
    centeredText(printInstruction,
                 topMillimeters: 266,
                 fontSize: 9,
                 weight: .regular,
                 context: context)
}

private func drawAprilTagSheet(_ fixture: Fixture,
                               layout: FixtureLayout,
                               context: CGContext) throws {
    let modules = try loadTagModules(fixture.sourceTag)

    clearPage(context: context)
    drawAprilTag(modules, context: context)

    let locationName = layout == .dualA4 ? "RIGHT SHEET" : "RIGHT PANEL"
    let mountInstruction = layout == .dualA4
        ? "<- Join this LEFT page edge to the QR sheet"
        : "A3 single sheet / AprilTag right of QR / center distance 210 mm"
    let printInstruction = layout == .dualA4
        ? "A4 portrait / 100% Actual Size / disable Fit or Scale"
        : "A3 landscape / 100% Actual Size / disable Fit or Scale"

    centeredText("\(locationName) / AprilTag / MarkerID \(fixture.markerID)",
                 topMillimeters: 20,
                 fontSize: 14,
                 weight: .semibold,
                 context: context)
    centeredText("\(fixture.kind.uppercased()) / tagStandard41h12 / image 160 mm",
                 topMillimeters: 30,
                 fontSize: 11,
                 weight: .medium,
                 context: context)
    // 位姿求解吃的是检测四边形，不是图幅。把要量的那条边和标称值一并印在纸上，
    // 免得现场量错对象——这是 88.9 mm，不是 160 mm。
    centeredText(String(format:
        "MEASURE the white ring outer-to-outer square: %.1f mm nominal (5/9 of image)",
        tagDetectionSizeMillimeters),
                 topMillimeters: 40,
                 fontSize: 11,
                 weight: .medium,
                 context: context)
    centeredText(mountInstruction,
                 topMillimeters: 252,
                 fontSize: 9,
                 weight: .regular,
                 context: context)
    centeredText(printInstruction,
                 topMillimeters: 266,
                 fontSize: 9,
                 weight: .regular,
                 context: context)
}

private func writeDualA4PDF(_ fixture: Fixture, to url: URL) throws {
    var mediaBox = CGRect(origin: .zero, size: pageSize)
    guard let context = CGContext(url as CFURL, mediaBox: &mediaBox, nil) else {
        throw FixtureError.contextCreation(url)
    }

    context.beginPDFPage(nil)
    try drawQrSheet(fixture, layout: .dualA4, context: context)
    context.endPDFPage()
    context.beginPDFPage(nil)
    try drawAprilTagSheet(fixture, layout: .dualA4, context: context)
    context.endPDFPage()
    context.closePDF()
}

private func writeA3LandscapePDF(_ fixture: Fixture, to url: URL) throws {
    var mediaBox = CGRect(origin: .zero, size: a3LandscapeSize)
    guard let context = CGContext(url as CFURL, mediaBox: &mediaBox, nil) else {
        throw FixtureError.contextCreation(url)
    }

    context.beginPDFPage(nil)
    try drawQrSheet(fixture, layout: .a3Landscape, context: context)
    context.saveGState()
    context.translateBy(x: pageSize.width, y: 0)
    try drawAprilTagSheet(fixture, layout: .a3Landscape, context: context)
    context.restoreGState()
    context.endPDFPage()
    context.closePDF()
}

private func writePNG(_ fixture: Fixture,
                      layout: FixtureLayout,
                      to url: URL) throws {
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    guard let context = CGContext(data: nil,
                                  width: previewWidth,
                                  height: previewHeight,
                                  bitsPerComponent: 8,
                                  bytesPerRow: 0,
                                  space: colorSpace,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
        throw FixtureError.contextCreation(url)
    }

    context.scaleBy(x: CGFloat(previewPageWidth) / pageSize.width,
                    y: CGFloat(previewHeight) / pageSize.height)
    try drawQrSheet(fixture, layout: layout, context: context)
    context.saveGState()
    context.translateBy(x: pageSize.width, y: 0)
    try drawAprilTagSheet(fixture, layout: layout, context: context)
    context.restoreGState()

    guard let image = context.makeImage() else {
        throw FixtureError.imageCreation(url)
    }
    guard let destination = CGImageDestinationCreateWithURL(url as CFURL,
                                                            UTType.png.identifier as CFString,
                                                            1,
                                                            nil) else {
        throw FixtureError.imageDestination(url)
    }
    CGImageDestinationAddImage(destination, image, [
        kCGImagePropertyDPIWidth: 300,
        kCGImagePropertyDPIHeight: 300
    ] as CFDictionary)
    guard CGImageDestinationFinalize(destination) else {
        throw FixtureError.imageWrite(url)
    }
}

/// 把标图按模块采成 0/1 位图。用于把"印出来的标和源图一致"变成可执行的检查。
/// 按模块采样，第 0 行是视觉顶部。
///
/// `CGContext.draw(_:in:)` 画进位图上下文后，内存第 0 行就是图像顶行——源标图和预览 PNG
/// 都一样，不需要区别对待。这一点由不经 CoreGraphics 的独立 PNG 解码器核对过：
/// PNG 的行本来就是自顶向下的。
private func sampleModules(_ image: CGImage,
                           originMillimeters: CGPoint,
                           sideMillimeters: CGFloat,
                           pageWidthMillimeters: CGFloat) -> [String] {
    let width = image.width, height = image.height
    var pixels = [UInt8](repeating: 0, count: width * height)
    let gray = CGColorSpaceCreateDeviceGray()
    guard let context = CGContext(data: &pixels,
                                  width: width,
                                  height: height,
                                  bitsPerComponent: 8,
                                  bytesPerRow: width,
                                  space: gray,
                                  bitmapInfo: CGImageAlphaInfo.none.rawValue) else {
        return []
    }
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    let pixelsPerMillimeter = CGFloat(width) / pageWidthMillimeters
    let step = sideMillimeters * pixelsPerMillimeter / CGFloat(tagTotalWidthModules)
    let x0 = originMillimeters.x * pixelsPerMillimeter
    let y0 = originMillimeters.y * pixelsPerMillimeter

    var rows: [String] = []
    for row in 0..<Int(tagTotalWidthModules) {
        var line = ""
        for column in 0..<Int(tagTotalWidthModules) {
            let x = Int(x0 + (CGFloat(column) + 0.5) * step)
            let y = Int(y0 + (CGFloat(row) + 0.5) * step)
            line += pixels[y * width + x] > 127 ? "." : "#"
        }
        rows.append(line)
    }
    return rows
}

/// 自检：从刚写出的预览 PNG 里把标采回来，和源标图逐模块比。
///
/// 这道守卫存在的理由很具体：CG 的 y 轴朝上而位图第 0 行在顶部，画的时候少翻一次坐标系，
/// 标就是**上下镜像**的。镜像不是旋转，apriltag 解不了——印出来一个都检不出，而纸面上
/// 肉眼完全看不出哪里不对。这个错误的代价是一次白打印加一轮白测。
private func verifyRenderedTag(previewURL: URL, sourceTag: URL) throws {
    let preview = try loadTagImage(previewURL)
    let source = try loadTagImage(sourceTag)

    let expected = sampleModules(source,
                                 originMillimeters: .zero,
                                 sideMillimeters: 9.0,
                                 pageWidthMillimeters: 9.0)
    let tagOriginX = pageSize.width / pointsPerMillimeter + (210.0 - 160.0) / 2.0
    // 采样按视觉自顶向下，所以这里要的是标顶边到页面顶边的距离，
    // 而 codeBottom 是从页面底边量的。A4 上下对称使两者数值相同，写全式子免得靠对称蒙对。
    let tagOriginY = 297.0 - codeBottom / pointsPerMillimeter - 160.0
    let rendered = sampleModules(preview,
                                 originMillimeters: CGPoint(x: tagOriginX, y: tagOriginY),
                                 sideMillimeters: 160.0,
                                 pageWidthMillimeters: 420.0)

    guard !expected.isEmpty, rendered == expected else {
        FileHandle.standardError.write(Data("""
        Tag verification FAILED for \(previewURL.lastPathComponent)
        expected:
        \(expected.joined(separator: "\n"))
        rendered:
        \(rendered.joined(separator: "\n"))

        """.utf8))
        throw FixtureError.tagVerificationFailed(previewURL)
    }
}

private func run() throws {
    guard CommandLine.arguments.count == 2 else {
        throw FixtureError.usage
    }

    let outputDirectory = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
    try FileManager.default.createDirectory(at: outputDirectory,
                                            withIntermediateDirectories: true)

    // 标图随仓库走，不再从命令行传：它们是 AprilRobotics/apriltag-imgs 的官方 9x9 位图，
    // 已逐模块比对过原生库 apriltag_to_image 的输出。当参数传等于每次都要记住哪张图配哪个 ID。
    let tagDirectory = URL(fileURLWithPath: CommandLine.arguments[0])
        .deletingLastPathComponent()
        .appendingPathComponent("tags", isDirectory: true)

    let fixtures = [
        Fixture(markerID: "0",
                kind: "static",
                sourceTag: tagDirectory.appendingPathComponent("tag41_12_00000.png"),
                outputStem: "pico_qr_apriltag_static_id0_a4"),
        Fixture(markerID: "250",
                kind: "dynamic",
                sourceTag: tagDirectory.appendingPathComponent("tag41_12_00250.png"),
                outputStem: "pico_qr_apriltag_dynamic_id250_a4")
    ]

    for fixture in fixtures {
        let pdfURL = outputDirectory.appendingPathComponent(fixture.outputStem).appendingPathExtension("pdf")
        let pngURL = outputDirectory.appendingPathComponent(fixture.outputStem + "_preview_300dpi").appendingPathExtension("png")
        let a3Stem = fixture.outputStem.replacingOccurrences(of: "_a4", with: "_a3_landscape")
        let a3PdfURL = outputDirectory.appendingPathComponent(a3Stem).appendingPathExtension("pdf")
        let a3PngURL = outputDirectory.appendingPathComponent(a3Stem + "_preview_300dpi").appendingPathExtension("png")

        try writeDualA4PDF(fixture, to: pdfURL)
        try writePNG(fixture, layout: .dualA4, to: pngURL)
        try writeA3LandscapePDF(fixture, to: a3PdfURL)
        try writePNG(fixture, layout: .a3Landscape, to: a3PngURL)
        // 两种版式各验一次：预览 PNG 走的是和 PDF 同一条绘制路径，
        // 所以它验过就等于 PDF 也对。
        try verifyRenderedTag(previewURL: pngURL, sourceTag: fixture.sourceTag)
        try verifyRenderedTag(previewURL: a3PngURL, sourceTag: fixture.sourceTag)

        print("Wrote \(pdfURL.path)")
        print("Wrote \(pngURL.path)")
        print("Wrote \(a3PdfURL.path)")
        print("Wrote \(a3PngURL.path)")
        print("  tag verified against \(fixture.sourceTag.lastPathComponent)")
    }
}

do {
    try run()
} catch {
    fputs("error: \(error)\n", stderr)
    exit(1)
}
