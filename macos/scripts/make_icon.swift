// Renders the DiskWatch app icon in the FloydNet Terminal style:
// black ground, hairline frame, stretched Times New Roman "D", capacity gauge with a threshold marker.
import AppKit

let size: CGFloat = 1024
let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "icon_1024.png"
// --square: full-bleed square plate with sharp corners (Windows); default is the macOS rounded plate.
let square = CommandLine.arguments.contains("--square")
func hex(_ v: UInt32) -> NSColor {
    NSColor(srgbRed: CGFloat((v >> 16) & 0xFF) / 255, green: CGFloat((v >> 8) & 0xFF) / 255, blue: CGFloat(v & 0xFF) / 255, alpha: 1)
}

let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size), pixelsHigh: Int(size), bitsPerSample: 8,
                           samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                           bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
let ctx = NSGraphicsContext.current!.cgContext

// macOS icon grid plate (the platform's mask shape), then everything inside is square.
let plate = square ? NSRect(x: 0, y: 0, width: size, height: size) : NSRect(x: 100, y: 100, width: 824, height: 824)
hex(0x000000).setFill()
if square { NSBezierPath(rect: plate).fill() } else { NSBezierPath(roundedRect: plate, xRadius: 185, yRadius: 185).fill() }

// Hairline compartment.
let frame = square ? plate.insetBy(dx: 110, dy: 110) : plate.insetBy(dx: 96, dy: 96)
hex(0x2A2A2A).setStroke()
let fp = NSBezierPath(rect: frame)
fp.lineWidth = 6
fp.stroke()

// Stretched serif "D".
let font = NSFont(name: "TimesNewRomanPSMT", size: 330) ?? NSFont.systemFont(ofSize: 330)
let str = NSAttributedString(string: "D", attributes: [.font: font, .foregroundColor: hex(0xF2F2F2)])
ctx.saveGState()
ctx.translateBy(x: frame.minX + 50, y: frame.minY + 170)
ctx.scaleBy(x: 1, y: 1.55)
str.draw(at: .zero)
ctx.restoreGState()

// Capacity gauge: track, accent fill, accent-bright threshold marker.
let bar = NSRect(x: frame.minX + 56, y: frame.minY + 110, width: frame.width - 112, height: 64)
hex(0x111111).setFill(); bar.fill()
hex(0x17D97A).setFill(); NSRect(x: bar.minX, y: bar.minY, width: bar.width * 0.62, height: bar.height).fill()
hex(0x444444).setStroke()
let bp = NSBezierPath(rect: bar)
bp.lineWidth = 5
bp.stroke()
hex(0x3DFFA0).setFill()
NSRect(x: bar.minX + bar.width * 0.9 - 6, y: bar.minY - 22, width: 12, height: bar.height + 44).fill()

NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
print("wrote \(out)")
