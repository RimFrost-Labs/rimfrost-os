#!/bin/bash
# Turns a Bazzite image into RimFrost OS. Runs inside the image build.
# IMAGE_NAME (rimfrost-os or rimfrost-os-nvidia) and IMAGE_VENDOR come from
# the Containerfile's build arguments.

set -ouex pipefail

IMAGE_NAME="${IMAGE_NAME:-rimfrost-os}"
IMAGE_VENDOR="${IMAGE_VENDOR:-rimfrost-labs}"
WALLPAPER=/usr/share/wallpapers/RimFrost/contents/images/3840x2160.png

# Our files: wallpaper, logo, signing key, container policy pieces
cp -avf "/ctx/system_files"/. /
# Set modes explicitly: programs 755, everything else 644, directories 755
# (sources checked out on Windows would otherwise carry 777).
( cd /ctx/system_files && find . -mindepth 1 ) | while read -r rel; do
    path="/${rel#./}"
    if [ -d "$path" ] && [ ! -L "$path" ]; then
        chmod 755 "$path"
    elif [ -f "$path" ] && [ ! -L "$path" ]; then
        case "$path" in
            /usr/bin/*|/usr/libexec/*) chmod 755 "$path" ;;
            *) chmod 644 "$path" ;;
        esac
    fi
done

### Identity ###################################################################
# Keep ID=bazzite so Bazzite's own tools (ujust, the updater) keep working;
# change what people see.
# Our own release number. The first public release is a beta: 0.5.
RIMFROST_VERSION="0.5 Beta"
BASE_VERSION=$(sed -n 's/^OSTREE_VERSION=//p' /usr/lib/os-release | tr -d "'\"")
sed -i \
    -e 's|^NAME=.*|NAME="RimFrost OS"|' \
    -e "s|^PRETTY_NAME=.*|PRETTY_NAME=\"RimFrost OS ${RIMFROST_VERSION}\"|" \
    -e 's|^DEFAULT_HOSTNAME=.*|DEFAULT_HOSTNAME="rimfrost"|' \
    -e 's|^HOME_URL=.*|HOME_URL="https://github.com/RimFrost-Labs/rimfrost-os"|' \
    -e 's|^BUG_REPORT_URL=.*|BUG_REPORT_URL="https://github.com/RimFrost-Labs/rimfrost-os/issues"|' \
    -e 's|^ANSI_COLOR=.*|ANSI_COLOR="0;38;2;170;214;240"|' \
    -e 's|^LOGO=.*|LOGO=rimfrost-logo|' \
    -e "s|^BOOTLOADER_NAME=.*|BOOTLOADER_NAME=\"RimFrost OS ${RIMFROST_VERSION} (${BASE_VERSION})\"|" \
    /usr/lib/os-release
echo "RIMFROST_IMAGE=\"${IMAGE_NAME}\"" >> /usr/lib/os-release
echo "RIMFROST_VERSION=\"${RIMFROST_VERSION}\"" >> /usr/lib/os-release
# The initramfs still carries Bazzite's os-release, so systemd's fallback
# hostname would stay "bazzite"; ship a real default instead.
echo rimfrost > /etc/hostname
# Bazzite's first-boot setup falls back to this name when the hostname is long.
sed -i 's/hostnamectl set-hostname bazzite/hostnamectl set-hostname rimfrost/' /usr/libexec/bazzite-hardware-setup

# The updater reads this to know which image to pull next time.
jq --arg name "$IMAGE_NAME" --arg vendor "$IMAGE_VENDOR" \
   '.["image-name"]=$name | .["image-vendor"]=$vendor
    | .["image-ref"]="ostree-image-signed:docker://ghcr.io/\($vendor)/\($name)"
    | .["image-tag"]="latest"' \
   /usr/share/ublue-os/image-info.json > /tmp/image-info.json
mv /tmp/image-info.json /usr/share/ublue-os/image-info.json

### Signed updates #############################################################
# Only accept RimFrost images signed with our key.
jq --arg repo "ghcr.io/${IMAGE_VENDOR}" \
   '.transports.docker[$repo] = [{
        "type": "sigstoreSigned",
        "keyPath": "/etc/pki/containers/rimfrost-os.pub",
        "signedIdentity": {"type": "matchRepository"}
    }]' /etc/containers/policy.json > /tmp/policy.json
mv /tmp/policy.json /etc/containers/policy.json

### Look #######################################################################
for js in /usr/share/plasma/look-and-feel/com.valve.*/contents/plasmoidsetupscripts/org.kde.plasma.folder.js; do
    sed -i "s|/usr/share/wallpapers/convergence.jxl|${WALLPAPER}|" "$js"
done
sed -i "s|/usr/share/wallpapers/convergence.jxl|${WALLPAPER}|g" /etc/xdg/kscreenlockerrc
# The splash after login: our animated mark, for new users and as the default.
for defaults in /usr/share/plasma/look-and-feel/com.valve.*/contents/defaults; do
    sed -i 's|^Theme=com.valve.vapor$|Theme=org.rimfrost.splash|' "$defaults"
done
printf '[KSplash]\nEngine=KSplashQML\nTheme=org.rimfrost.splash\n' > /etc/xdg/ksplashrc
for size in 64 256; do
    install -Dm644 "/usr/share/pixmaps/rimfrost-logo-${size}.png" \
        "/usr/share/icons/hicolor/${size}x${size}/apps/rimfrost-logo.png"
done

### Recorder ###################################################################
# GPU Screen Recorder (GPL-3, from Terra) is the capture engine ClipFrost
# drives as a separate program. Its gsr-kms-server carries cap_sys_admin, so
# screen capture needs no permission dialog.
dnf5 -y --enablerepo=terra install gpu-screen-recorder
# leave no package-manager state behind in /var and /run (bootc lint)
rm -rf /var/lib/dnf/repos /run/dnf

### ClipFrost ##################################################################
# Instant replay (proprietary, RimFrost Labs). The workflow checks it out into
# clipfrost-src; builds without access leave it out (REQUIRE_CLIPFROST=1 makes
# that an error).
CF=/ctx/clipfrost-src
if [ -f "$CF/clipfrost/clipfrostd" ]; then
    install -d /usr/lib/clipfrost/clipfrost /usr/share/licenses/clipfrost
    install -m644 "$CF"/clipfrost/__init__.py "$CF"/clipfrost/engine.py /usr/lib/clipfrost/clipfrost/
    install -m755 "$CF"/clipfrost/clipfrostd "$CF"/clipfrost/clipfrost "$CF"/clipfrost/clipfrost-settings         /usr/lib/clipfrost/clipfrost/
    install -m644 "$CF"/clipfrost/settings.qml /usr/lib/clipfrost/clipfrost/
    ln -sf /usr/lib/clipfrost/clipfrost/clipfrost-settings /usr/bin/clipfrost-settings
    install -m644 "$CF"/data/clipfrost.desktop /usr/share/applications/clipfrost.desktop
    install -m755 "$CF"/clipfrost/clipfrost-hook /usr/libexec/clipfrost-hook
    ln -sf /usr/lib/clipfrost/clipfrost/clipfrostd /usr/libexec/clipfrostd
    ln -sf /usr/lib/clipfrost/clipfrost/clipfrost /usr/bin/clipfrost
    install -m644 "$CF"/data/clipfrostd.service /usr/lib/systemd/user/clipfrostd.service
    install -m644 "$CF"/data/clipfrost-save.desktop /usr/share/applications/clipfrost-save.desktop
    printf 'ClipFrost (c) RimFrost Labs. All rights reserved. Not covered by the
Apache 2.0 licence of RimFrost OS.
' > /usr/share/licenses/clipfrost/LICENSE
    systemctl --global enable clipfrostd.service
elif [ "${REQUIRE_CLIPFROST:-0}" = 1 ]; then
    echo "ClipFrost sources missing" >&2
    exit 1
fi

### Welcome guide ##############################################################
# RimFrost's short first-login guide replaces Bazzite Portal's autostart; the
# Portal stays available from the guide's last page ("More apps and tools").
chmod 755 /usr/libexec/rimfrost-welcome
ln -sf /usr/libexec/rimfrost-welcome /usr/bin/rimfrost-welcome
rm -f /etc/skel/.config/autostart/bazzite-portal.desktop
# Anti-cheat status per game: Are We Anti-Cheat Yet (MIT). Fresh at every
# build; the guide refreshes it weekly online.
curl -fsSL --retry 3 -o /usr/share/rimfrost/welcome/awacy.json     https://raw.githubusercontent.com/AreWeAntiCheatYet/AreWeAntiCheatYet/HEAD/games.json
python3 -c 'import json; assert len(json.load(open("/usr/share/rimfrost/welcome/awacy.json"))) > 500'
printf 'Anti-cheat data: Are We Anti-Cheat Yet, https://areweanticheatyet.com\nMIT License, Copyright (c) AreWeAntiCheatYet contributors\n' \
    > /usr/share/rimfrost/welcome/awacy.LICENSE

### Test telemetry ###########################################################
# Off unless a test machine is enrolled (sudo rimfrost-telemetry enroll ...).
chmod 755 /usr/libexec/rimfrost-telemetry
ln -sf /usr/libexec/rimfrost-telemetry /usr/bin/rimfrost-telemetry
install -m755 /ctx/framemeter/librimfrost_framemeter.so /usr/lib64/librimfrost_framemeter.so
install -m644 /ctx/framemeter/rimfrost_framemeter.json     /usr/share/vulkan/implicit_layer.d/rimfrost_framemeter.x86_64.json

### Finish setup on the first start ########################################
# One ISO for every PC: on a current Nvidia card the installer points updates
# at the Nvidia system, and this fetches it and restarts into it.
systemctl enable rimfrost-finish-setup.service
# Installed with RimFrost Setup (the Windows installer): remove its installer
# partitions and ESP entry, and grow into their room.
systemctl enable rimfrost-setup-cleanup.service

### Package sources ##########################################################
# Bazzite ships the terra repos disabled. bootc-image-builder still reads them
# when making the ISO and fails on their file:// GPG keys, so keep them out of
# /etc/yum.repos.d. (Updates come as whole images; these repos only matter for
# someone layering packages, who can copy them back.)
mkdir -p /usr/share/rimfrost/disabled-repos
for repo in /etc/yum.repos.d/terra*.repo; do
    [ -e "$repo" ] && mv "$repo" /usr/share/rimfrost/disabled-repos/
done

### Game mode ##################################################################
# rimfrost-gamemoded gives the focused game the CPU and disk (cgroup weights)
# and holds the performance power profile; a KWin script reports focus.
chmod 755 /usr/libexec/rimfrost-gamemoded /usr/libexec/rimfrost-bench /usr/bin/rimfrost-gamemode
systemctl --global enable rimfrost-gamemoded.service
mkdir -p /etc/xdg
printf '[Plugins]\nrimfrost-focusEnabled=true\n' >> /etc/xdg/kwinrc
# MangoHud (FPS overlay, frame-time logs for measuring game mode) comes with
# Bazzite as terra-mangohud.

### Initramfs ##################################################################
# Rebuild it with Bazzite's own dracut settings so early boot carries our
# os-release (systemd's fallback hostname comes from there, and the installer
# leaves /etc/hostname empty).
KVER=$(ls /usr/lib/modules | head -1)
export DRACUT_NO_XATTR=1
# /root points at /var/roothome, which only exists on a booted system
mkdir -p /var/roothome
dracut --no-hostonly --kver "$KVER" --reproducible --zstd --add ostree -f "/usr/lib/modules/$KVER/initramfs.img"
chmod 0600 "/usr/lib/modules/$KVER/initramfs.img"
rmdir /var/roothome

### Checks #####################################################################
grep -q 'RimFrost OS' /usr/lib/os-release
grep -q 'set-hostname rimfrost' /usr/libexec/bazzite-hardware-setup
grep -q "$WALLPAPER" /etc/xdg/kscreenlockerrc
jq -e --arg repo "ghcr.io/${IMAGE_VENDOR}" '.transports.docker[$repo]' /etc/containers/policy.json
test -f "$WALLPAPER"
grep -q 'Theme=org.rimfrost.splash' /etc/xdg/ksplashrc
grep -q 'Theme=org.rimfrost.splash' /usr/share/plasma/look-and-feel/com.valve.vapor.desktop/contents/defaults
python3 -c 'import ast, sys; [ast.parse(open(f).read(), f) for f in sys.argv[1:]]' \
    /usr/libexec/rimfrost-gamemoded /usr/bin/rimfrost-gamemode /usr/libexec/rimfrost-bench
test -f /usr/share/kwin/scripts/rimfrost-focus/contents/code/main.js
systemctl --global is-enabled rimfrost-gamemoded.service
command -v mangohud
! ls /etc/yum.repos.d/terra*.repo 2>/dev/null
# (no "| grep -q" on long output: grep quits early, the writer gets SIGPIPE, pipefail fails)
lsinitrd -f usr/lib/initrd-release "/usr/lib/modules/$KVER/initramfs.img" > /tmp/initrd-release
grep -q '^DEFAULT_HOSTNAME="rimfrost"' /tmp/initrd-release
[[ "$(getcap /usr/bin/gsr-kms-server)" == *cap_sys_admin* ]]
! systemctl is-enabled rimfrost-telemetry.service >/dev/null 2>&1
systemctl is-enabled rimfrost-finish-setup.service
systemctl is-enabled rimfrost-setup-cleanup.service
bash -n /usr/libexec/rimfrost-setup-cleanup
bash -n /usr/libexec/rimfrost-finish-setup
command -v jq notify-send
test -x /usr/lib64/librimfrost_framemeter.so
test -f /etc/skel/.config/autostart/rimfrost-welcome.desktop
! test -e /etc/skel/.config/autostart/bazzite-portal.desktop
