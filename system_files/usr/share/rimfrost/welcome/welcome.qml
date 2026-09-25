// RimFrost welcome: five short pages after the first login.
import QtQuick
import QtQuick.Layouts
import QtQuick.Controls as QQC2
import org.kde.kirigami as Kirigami

Kirigami.ApplicationWindow {
    id: win
    title: "Welcome to RimFrost OS"
    width: Kirigami.Units.gridUnit * 40
    height: Kirigami.Units.gridUnit * 32
    minimumWidth: Kirigami.Units.gridUnit * 28
    minimumHeight: Kirigami.Units.gridUnit * 26

    property int page: backend.startPage
    readonly property int pages: 6

    component PageTitle: ColumnLayout {
        property alias title: t.text
        property alias text: d.text
        spacing: Kirigami.Units.largeSpacing
        Layout.fillWidth: true
        Kirigami.Heading {
            id: t; level: 1; wrapMode: Text.WordWrap; Layout.fillWidth: true
            font.pointSize: Kirigami.Theme.defaultFont.pointSize * 1.9
            font.weight: Font.DemiBold
        }
        QQC2.Label {
            id: d; wrapMode: Text.WordWrap; Layout.fillWidth: true; opacity: 0.85
            font.pointSize: Kirigami.Theme.defaultFont.pointSize * 1.1
            Layout.bottomMargin: Kirigami.Units.largeSpacing
        }
    }

    pageStack.initialPage: Kirigami.Page {
        title: ""
        globalToolBarStyle: Kirigami.ApplicationHeaderStyle.None
        padding: Kirigami.Units.gridUnit * 2

        ColumnLayout {
            anchors.fill: parent
            spacing: Kirigami.Units.largeSpacing

            StackLayout {
                id: stack
                currentIndex: win.page
                Layout.fillWidth: true
                Layout.fillHeight: true

                // 0: welcome
                Item {
                ColumnLayout {
                    anchors.centerIn: parent
                    width: Math.min(parent.width, Kirigami.Units.gridUnit * 28)
                    spacing: Kirigami.Units.largeSpacing * 2
                    Image {
                        source: "file://" + backend.markPath
                        sourceSize.width: Kirigami.Units.gridUnit * 6
                        sourceSize.height: Kirigami.Units.gridUnit * 6
                        Layout.alignment: Qt.AlignHCenter
                    }
                    Kirigami.Heading {
                        text: "Welcome to RimFrost OS"
                        level: 1
                        font.pointSize: Kirigami.Theme.defaultFont.pointSize * 2.4
                        font.weight: Font.DemiBold
                        Layout.alignment: Qt.AlignHCenter
                    }
                    QQC2.Label {
                        text: "Your PC is ready for gaming. Here are the four things worth knowing. It takes a minute."
                        wrapMode: Text.WordWrap
                        horizontalAlignment: Text.AlignHCenter
                        Layout.fillWidth: true
                        font.pointSize: Kirigami.Theme.defaultFont.pointSize * 1.15
                        Layout.alignment: Qt.AlignHCenter
                    }
                }
                }

                // 1: Steam
                ColumnLayout {
                    spacing: Kirigami.Units.largeSpacing
                    PageTitle {
                        title: "Your games live in Steam"
                        text: "Sign in to Steam and your library is right there. Most Windows games install and start with one click, just like on Windows."
                    }
                    Kirigami.InlineMessage {
                        Layout.fillWidth: true
                        visible: true
                        text: "If a game won't start: right-click it in Steam, open Properties, then Compatibility, and tick “Force the use of a specific Steam Play compatibility tool”."
                    }
                    QQC2.Button {
                        text: "Open Steam"
                        icon.name: "steam"
                        onClicked: backend.openSteam()
                    }
                    Item { Layout.fillHeight: true }
                }

                // 2: game mode
                ColumnLayout {
                    spacing: Kirigami.Units.largeSpacing
                    PageTitle {
                        title: "Game mode works on its own"
                        text: "When a game has your attention, RimFrost OS gives it the power of your PC, even if Discord, a browser or a download is running. Tab out, and everything else is smooth again. There is nothing to set up."
                    }
                    QQC2.Switch {
                        text: "Game mode"
                        checked: backend.gameMode
                        onToggled: backend.gameMode = checked
                    }
                    QQC2.Label {
                        text: backend.gameModeStatus
                        opacity: 0.7
                        wrapMode: Text.WordWrap
                        Layout.fillWidth: true
                    }
                    Item { Layout.fillHeight: true }
                }

                // 3: ClipFrost
                ColumnLayout {
                    spacing: Kirigami.Units.largeSpacing
                    PageTitle {
                        title: "Save your best moments"
                        text: backend.hasClipFrost
                            ? "ClipFrost keeps the last minute of your game in memory. Pulled off something great? Press " + backend.clipHotkey + " and it is saved, with game sound, your mic and voice chat on separate tracks."
                            : "Instant replay is coming to RimFrost OS soon."
                    }
                    RowLayout {
                        visible: backend.hasClipFrost
                        spacing: Kirigami.Units.largeSpacing
                        Rectangle {
                            radius: Kirigami.Units.smallSpacing
                            color: Kirigami.Theme.alternateBackgroundColor
                            border.color: Kirigami.Theme.disabledTextColor
                            implicitWidth: hk.implicitWidth + Kirigami.Units.gridUnit * 2
                            implicitHeight: hk.implicitHeight + Kirigami.Units.gridUnit
                            QQC2.Label { id: hk; anchors.centerIn: parent; text: backend.clipHotkey; font.bold: true; font.pointSize: 16 }
                        }
                        QQC2.Label { text: "saves the last minute"; opacity: 0.8 }
                    }
                    QQC2.Button {
                        visible: backend.hasClipFrost
                        text: "ClipFrost settings"
                        icon.name: "configure"
                        onClicked: backend.openClipFrost()
                    }
                    Item { Layout.fillHeight: true }
                }

                // 4: your games
                ColumnLayout {
                    spacing: Kirigami.Units.largeSpacing
                    PageTitle {
                        title: "Your games"
                        text: backend.libraryIntro
                    }
                    QQC2.ScrollView {
                        Layout.fillWidth: true
                        Layout.fillHeight: true
                        visible: backend.games.length > 0
                        ListView {
                            model: backend.games
                            clip: true
                            spacing: 2
                            delegate: RowLayout {
                                required property var modelData
                                width: ListView.view.width
                                spacing: Kirigami.Units.largeSpacing
                                Kirigami.Icon {
                                    source: modelData.ok ? "dialog-ok-apply" : "dialog-warning"
                                    color: modelData.ok ? Kirigami.Theme.positiveTextColor : Kirigami.Theme.neutralTextColor
                                    implicitWidth: Kirigami.Units.iconSizes.small
                                    implicitHeight: Kirigami.Units.iconSizes.small
                                }
                                QQC2.Label { text: modelData.name; Layout.fillWidth: true; elide: Text.ElideRight }
                                QQC2.Label {
                                    text: modelData.status
                                    color: modelData.ok ? Kirigami.Theme.positiveTextColor : Kirigami.Theme.neutralTextColor
                                }
                            }
                        }
                    }
                    RowLayout {
                        QQC2.Button {
                            text: "Check again"
                            icon.name: "view-refresh"
                            onClicked: backend.scanLibrary()
                        }
                        QQC2.Button {
                            text: "Look up any game"
                            icon.name: "internet-services"
                            onClicked: backend.openUrl("https://areweanticheatyet.com/")
                        }
                    }
                    QQC2.Label {
                        text: "Anti-cheat data from Are We Anti-Cheat Yet (MIT licence)."
                        font.pointSize: Kirigami.Theme.smallFont.pointSize
                        opacity: 0.6
                    }
                }

                // 5: done
                ColumnLayout {
                    spacing: Kirigami.Units.largeSpacing
                    PageTitle {
                        title: "You're all set"
                        text: "Have fun. You can open this guide again any time from the app menu: search for “Welcome”."
                    }
                    QQC2.Button {
                        text: "More apps and tools"
                        icon.name: "applications-other"
                        visible: backend.hasPortal
                        onClicked: backend.openPortal()
                    }
                    QQC2.Button {
                        text: "Report a problem"
                        icon.name: "tools-report-bug"
                        onClicked: backend.openUrl("https://github.com/RimFrost-Labs/rimfrost-os/issues")
                    }
                    Item { Layout.fillHeight: true }
                }
            }

            // footer: progress and navigation
            RowLayout {
                Layout.fillWidth: true
                QQC2.Button {
                    text: "Back"
                    icon.name: "go-previous"
                    enabled: win.page > 0
                    onClicked: win.page--
                }
                Item { Layout.fillWidth: true }
                Row {
                    spacing: Kirigami.Units.smallSpacing
                    Repeater {
                        model: win.pages
                        Rectangle {
                            width: 8; height: 8; radius: 4
                            color: index === win.page ? Kirigami.Theme.textColor : Kirigami.Theme.disabledTextColor
                            opacity: index === win.page ? 1 : 0.4
                        }
                    }
                }
                Item { Layout.fillWidth: true }
                QQC2.Button {
                    text: win.page === win.pages - 1 ? "Start playing" : "Next"
                    icon.name: win.page === win.pages - 1 ? "dialog-ok-apply" : "go-next"
                    highlighted: true
                    onClicked: {
                        if (win.page === win.pages - 1) { backend.finish(); Qt.quit(); }
                        else win.page++;
                    }
                }
            }
        }
    }
}
