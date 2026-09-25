/*
    RimFrost OS splash: the mark fades in and turns once around its own
    vertical axis, then slides left while "RimFrost OS" is revealed to its
    right. A thin line underneath follows Plasma's startup stages.

    SPDX-License-Identifier: Apache-2.0
*/

import QtQuick
import org.kde.kirigami as Kirigami

Rectangle {
    id: root
    color: "#080E1C"

    property int stage
    readonly property bool animate: Kirigami.Units.longDuration > 1

    readonly property real markSize: Math.round(Math.min(width, height) * 0.16)
    readonly property real gap: Math.round(markSize * 0.28)
    readonly property real groupWidth: markSize + gap + word.implicitWidth
    // where the mark starts (centred alone) and ends (left part of the lockup)
    readonly property real markStartX: (width - markSize) / 2
    readonly property real markEndX: (width - groupWidth) / 2

    // a soft ice glow behind the lockup
    Image {
        anchors.centerIn: parent
        width: root.markSize * 6
        height: width
        source: "images/glow.png"
        smooth: true
    }

    Item {
        id: mark
        width: root.markSize
        height: root.markSize
        x: root.markStartX
        y: (root.height - height) / 2
        opacity: 0

        Image {
            anchors.fill: parent
            source: "images/rimfrost-mark.svg"
            sourceSize.width: width * 2
            sourceSize.height: height * 2
            smooth: true
            mipmap: true
        }

        transform: Rotation {
            id: spin
            origin.x: mark.width / 2
            origin.y: mark.height / 2
            axis { x: 0; y: 1; z: 0 }
            angle: 0
        }
    }

    // the wordmark, revealed from left to right by widening its clip
    Item {
        id: reveal
        x: mark.x + root.markSize + root.gap
        anchors.verticalCenter: mark.verticalCenter
        height: word.implicitHeight
        width: 0
        clip: true

        Text {
            id: word
            text: "RimFrost <font color=\"#6F93AE\">OS</font>"
            textFormat: Text.StyledText
            color: "#AAD6F0"
            font.family: "Lato"
            font.weight: Font.Bold
            font.pixelSize: Math.round(root.markSize * 0.46)
            font.letterSpacing: Math.round(root.markSize * 0.46) * 0.04
        }
    }

    // startup progress
    Rectangle {
        id: track
        width: root.groupWidth
        height: 2
        radius: 1
        anchors.horizontalCenter: parent.horizontalCenter
        y: mark.y + root.markSize + root.markSize * 0.45
        color: "#122A4A"
        opacity: reveal.width > 0 ? 1 : 0
        Behavior on opacity { NumberAnimation { duration: 400 } }

        Rectangle {
            height: parent.height
            radius: 1
            color: "#AAD6F0"
            width: parent.width * Math.min(root.stage, 6) / 6
            Behavior on width { NumberAnimation { duration: 500; easing.type: Easing.OutCubic } }
        }
    }

    SequentialAnimation {
        id: intro
        running: false

        ParallelAnimation {
            NumberAnimation { target: mark; property: "opacity"; from: 0; to: 1; duration: 300; easing.type: Easing.OutQuad }
            NumberAnimation { target: spin; property: "angle"; from: 0; to: 360; duration: 900; easing.type: Easing.InOutCubic }
        }
        PauseAnimation { duration: 60 }
        ParallelAnimation {
            NumberAnimation { target: mark; property: "x"; to: root.markEndX; duration: 550; easing.type: Easing.InOutCubic }
            SequentialAnimation {
                PauseAnimation { duration: 120 }
                NumberAnimation { target: reveal; property: "width"; to: word.implicitWidth + 4; duration: 500; easing.type: Easing.OutCubic }
            }
        }
    }

    function finalState() {
        mark.opacity = 1;
        mark.x = root.markEndX;
        reveal.width = word.implicitWidth + 4;
    }

    Component.onCompleted: {
        if (animate) {
            intro.start();
        } else {
            finalState();
        }
    }

    // keep the lockup centred if the screen size changes after the intro
    onWidthChanged: if (!intro.running && reveal.width > 0) mark.x = root.markEndX
}
