import SwiftUI
import UserNotifications

final class NotificationDelegate: NSObject, UNUserNotificationCenterDelegate {
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }
}

@main struct CodeLinkApp: App {
    @StateObject private var model = LinkModel()
    @Environment(\.scenePhase) private var phase
    private let notificationDelegate = NotificationDelegate()
    #if os(macOS)
    @NSApplicationDelegateAdaptor(MacAppDelegate.self) private var delegate
    #endif
    var body: some Scene {
        WindowGroup {
            RootView().environmentObject(model)
                .onAppear { UNUserNotificationCenter.current().delegate = notificationDelegate }
                .onChange(of:phase) { _, phase in model.sceneActive(phase == .active) }
                #if os(macOS)
                .frame(minWidth:1000, minHeight:640)
                #endif
        }
        #if os(macOS)
        .defaultSize(width:1200, height:780)
        .commands { CommandGroup(replacing:.newItem) {} }
        #endif
    }
}
