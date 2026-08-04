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
private let sourceMarkerRect = CGRect(x: 70.87, y: 271.84, width: 453.54, height: 453.54)
private let markerSize = 160.0 * pointsPerMillimeter
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
    let sourcePDF: URL
    let outputStem: String
}

private enum FixtureLayout {
    case dualA4
    case a3Landscape
}

private enum FixtureError: Error, CustomStringConvertible {
    case usage
    case unreadablePDF(URL)
    case missingPage(URL)
    case qrGeneration(String)
    case contextCreation(URL)
    case imageCreation(URL)
    case imageDestination(URL)
    case imageWrite(URL)

    var description: String {
        switch self {
        case .usage:
            return "Usage: generate_pico_qr_aruco_fixtures.swift <A4_0_static.pdf> <A4_250_dynamic.pdf> <output-directory>"
        case .unreadablePDF(let url):
            return "Cannot open source PDF: \(url.path)"
        case .missingPage(let url):
            return "Source PDF has no first page: \(url.path)"
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

private func drawSourceMarker(from page: CGPDFPage, context: CGContext) {
    let targetRect = CGRect(
        x: (pageSize.width - markerSize) / 2.0,
        y: codeBottom,
        width: markerSize,
        height: markerSize
    )
    let scale = markerSize / sourceMarkerRect.width

    context.saveGState()
    context.clip(to: targetRect)
    context.translateBy(x: targetRect.minX, y: targetRect.minY)
    context.scaleBy(x: scale, y: scale)
    context.translateBy(x: -sourceMarkerRect.minX, y: -sourceMarkerRect.minY)
    context.drawPDFPage(page)
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
        ? "Join this RIGHT page edge to the ArUco sheet ->"
        : "A3 single sheet / QR left of ArUco / center distance 210 mm"
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

private func drawArUcoSheet(_ fixture: Fixture,
                            layout: FixtureLayout,
                            context: CGContext) throws {
    guard let document = CGPDFDocument(fixture.sourcePDF as CFURL) else {
        throw FixtureError.unreadablePDF(fixture.sourcePDF)
    }
    guard let page = document.page(at: 1) else {
        throw FixtureError.missingPage(fixture.sourcePDF)
    }

    clearPage(context: context)
    drawSourceMarker(from: page, context: context)

    let locationName = layout == .dualA4 ? "RIGHT SHEET" : "RIGHT PANEL"
    let mountInstruction = layout == .dualA4
        ? "<- Join this LEFT page edge to the QR sheet"
        : "A3 single sheet / ArUco right of QR / center distance 210 mm"
    let printInstruction = layout == .dualA4
        ? "A4 portrait / 100% Actual Size / disable Fit or Scale"
        : "A3 landscape / 100% Actual Size / disable Fit or Scale"

    centeredText("\(locationName) / ArUco / MarkerID \(fixture.markerID)",
                 topMillimeters: 20,
                 fontSize: 14,
                 weight: .semibold,
                 context: context)
    centeredText("\(fixture.kind.uppercased()) / DICT_4X4_1000 / outer size 160 mm",
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

private func writeDualA4PDF(_ fixture: Fixture, to url: URL) throws {
    var mediaBox = CGRect(origin: .zero, size: pageSize)
    guard let context = CGContext(url as CFURL, mediaBox: &mediaBox, nil) else {
        throw FixtureError.contextCreation(url)
    }

    context.beginPDFPage(nil)
    try drawQrSheet(fixture, layout: .dualA4, context: context)
    context.endPDFPage()
    context.beginPDFPage(nil)
    try drawArUcoSheet(fixture, layout: .dualA4, context: context)
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
    try drawArUcoSheet(fixture, layout: .a3Landscape, context: context)
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
    try drawArUcoSheet(fixture, layout: layout, context: context)
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

private func run() throws {
    guard CommandLine.arguments.count == 4 else {
        throw FixtureError.usage
    }

    let outputDirectory = URL(fileURLWithPath: CommandLine.arguments[3], isDirectory: true)
    try FileManager.default.createDirectory(at: outputDirectory,
                                            withIntermediateDirectories: true)

    let fixtures = [
        Fixture(markerID: "0",
                kind: "static",
                sourcePDF: URL(fileURLWithPath: CommandLine.arguments[1]),
                outputStem: "pico_qr_aruco_static_id0_a4"),
        Fixture(markerID: "250",
                kind: "dynamic",
                sourcePDF: URL(fileURLWithPath: CommandLine.arguments[2]),
                outputStem: "pico_qr_aruco_dynamic_id250_a4")
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
        print("Wrote \(pdfURL.path)")
        print("Wrote \(pngURL.path)")
        print("Wrote \(a3PdfURL.path)")
        print("Wrote \(a3PngURL.path)")
    }
}

do {
    try run()
} catch {
    fputs("error: \(error)\n", stderr)
    exit(1)
}
