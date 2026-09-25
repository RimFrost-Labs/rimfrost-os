// RimFrost game mode: tell rimfrost-gamemoded which window has focus.
// The daemon decides whether it belongs to a game and moves resources.

function report(window) {
    if (!window || !window.normalWindow) {
        return;
    }
    callDBus("org.rimfrost.GameMode", "/org/rimfrost/GameMode",
             "org.rimfrost.GameMode", "FocusChanged",
             window.pid, String(window.resourceClass), String(window.caption),
             window.fullScreen);
}

workspace.windowActivated.connect(report);
report(workspace.activeWindow);
