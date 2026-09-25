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

### Identity ###################################################################
# Keep ID=bazzite so Bazzite's own tools (ujust, the updater) keep working;
# change what people see.
BASE_VERSION=$(sed -n 's/^OSTREE_VERSION=//p' /usr/lib/os-release | tr -d "'\"")
sed -i \
    -e 's|^NAME=.*|NAME="RimFrost OS"|' \
    -e 's|^PRETTY_NAME=.*|PRETTY_NAME="RimFrost OS"|' \
    -e 's|^DEFAULT_HOSTNAME=.*|DEFAULT_HOSTNAME="rimfrost"|' \
    -e 's|^HOME_URL=.*|HOME_URL="https://github.com/RimFrost-Labs/rimfrost-os"|' \
    -e 's|^BUG_REPORT_URL=.*|BUG_REPORT_URL="https://github.com/RimFrost-Labs/rimfrost-os/issues"|' \
    -e 's|^ANSI_COLOR=.*|ANSI_COLOR="0;38;2;170;214;240"|' \
    -e 's|^LOGO=.*|LOGO=rimfrost-logo|' \
    -e "s|^BOOTLOADER_NAME=.*|BOOTLOADER_NAME=\"RimFrost OS (${BASE_VERSION})\"|" \
    /usr/lib/os-release
echo "RIMFROST_IMAGE=\"${IMAGE_NAME}\"" >> /usr/lib/os-release
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

### Checks #####################################################################
grep -q 'RimFrost OS' /usr/lib/os-release
grep -q 'set-hostname rimfrost' /usr/libexec/bazzite-hardware-setup
grep -q "$WALLPAPER" /etc/xdg/kscreenlockerrc
jq -e --arg repo "ghcr.io/${IMAGE_VENDOR}" '.transports.docker[$repo]' /etc/containers/policy.json
test -f "$WALLPAPER"
grep -q 'Theme=org.rimfrost.splash' /etc/xdg/ksplashrc
grep -q 'Theme=org.rimfrost.splash' /usr/share/plasma/look-and-feel/com.valve.vapor.desktop/contents/defaults
