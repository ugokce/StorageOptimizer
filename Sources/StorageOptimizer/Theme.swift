import AppKit
import StorageCore
import SwiftUI

extension Color {
    /// A color with separate values for light and dark appearance.
    init(light: UInt32, dark: UInt32) {
        self.init(nsColor: NSColor(name: nil) { appearance in
            let isDark = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            return NSColor(hex: isDark ? dark : light)
        })
    }
}

extension NSColor {
    convenience init(hex: UInt32) {
        self.init(
            srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: 1
        )
    }
}

extension FileCategory {
    /// Each category keeps the same color everywhere. The order of the eight hues was checked
    /// for color-vision-deficiency separation in both appearances; "Other" is neutral gray.
    var color: Color {
        switch self {
        case .document: Color(light: 0x2A78D6, dark: 0x3987E5)
        case .application: Color(light: 0xEB6834, dark: 0xD95926)
        case .image: Color(light: 0x1BAF7A, dark: 0x199E70)
        case .audio: Color(light: 0xEDA100, dark: 0xC98500)
        case .video: Color(light: 0xE87BA4, dark: 0xD55181)
        case .archive: Color(light: 0x008300, dark: 0x008300)
        case .developer: Color(light: 0x4A3AA7, dark: 0x9085E9)
        case .diskImage: Color(light: 0xE34948, dark: 0xE66767)
        case .other: Color(light: 0x898781, dark: 0x898781)
        }
    }
}

enum Palette {
    /// Space used by things outside the scanned folder (system files, other users, ...).
    static let unscanned = Color(light: 0xC3C2B7, dark: 0x4A4A46)
    static let track = Color.secondary.opacity(0.15)
}
