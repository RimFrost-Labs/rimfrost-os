#!/usr/bin/bash
conky -c /usr/share/conky/conky.conf &
is_swedish_locale() {
    [[ "${LC_MESSAGES:-${LANG:-C}}" == sv* ]]
}

# if CSM/Legacy show blocking message and power off
if [[ ! -d /sys/firmware/efi ]]; then
    legacy_boot_message="RimFrost OS har inte stöd för CSM/Legacy-start. Starta i inställningarna för UEFI/BIOS, inaktivera CSM/Legacy-läge och starta sedan om."
    shutdown_button="Stäng av"
    if is_swedish_locale; then
        legacy_boot_message="RimFrost OS har inte stöd för CSM/Legacy-start. Starta i inställningarna för UEFI/BIOS, inaktivera CSM/Legacy-läge och starta sedan om."
    else
        legacy_boot_message="RimFrost OS does not support CSM/Legacy Boot. Please boot into your UEFI/BIOS settings, disable CSM/Legacy Mode, and reboot."
        shutdown_button="Shutdown"
    fi
    yad --undecorated --on-top --timeout=0 --button="$shutdown_button:0" \
        --text="$legacy_boot_message" || true
    systemctl poweroff || shutdown -h now || true
fi
block_low_memory_install(){
memory=$(sudo cat /proc/meminfo | grep 'MemTotal' | cut -d ":" -f 2 | cut -d "k" -f 1 | sed 's/ //g')
gb_memory=$(
  awk '/^MemTotal/{print $2*1024}' < /proc/meminfo |
    numfmt --to=iec --format=%0f --suffix=B
)
echo "$memory"
echo "$gb_memory"
if [[ $memory -eq 0 ]]; then
 echo "could not determine memory. Exiting."
 return 1
elif [[ $memory -lt  5000000 ]]; then
 echo "detected memory less than approx. 5GB, warning user"
else
 return 0
fi
serve_docs
   if is_swedish_locale; then
       text="Du behöver <b>minst 8 GB systemminne</b> för att installera RimFrost OS.\n\nInstallationen misslyckas troligen med 4 GB minne eller mindre.\n\nIdentifierad mängd minne: $gb_memory\n\nLäs <a href=\"http://127.0.0.1:1290/Gaming/Hardware_compatibility_for_gaming/#minimum-system-requirements\">dokumentationen</a> om RimFrost OSs lägsta systemkrav."
       title="För lite minne"
       shutdown_button="Stäng av"
   else
       text="You need <b>at least 8GB of system memory</b>  to install RimFrost OS. \n\n Installation with 4GB or less memory will likely fail.\n\nDetected amount of memory: $gb_memory\n\n Please read <a href=\"http://127.0.0.1:1290/Gaming/Hardware_compatibility_for_gaming/#minimum-system-requirements\">here</a> about minimum system requirements for RimFrost OS."
       title="Not enough memory"
       shutdown_button="Shutdown"
   fi
    while true; do
    yad --undecorated --on-top --timeout=0 --button="$shutdown_button:0"  --warning --buttons-layout=center --text-align=center --title="$title" --text="$text"
     case $? in
            0)  systemctl poweroff || shutdown -h now || true
            break
            ;;
    esac
done
}
serve_docs() {
    ADDRESS=127.0.0.1
    PORT=1290
    { python -m http.server -b $ADDRESS $PORT -d /usr/share/ublue-os/docs/html; } >/dev/null 2>&1 &
    if [[ $- == *i* ]]; then
        fg >/dev/null 2>&1 || true
    fi
}
# RimFrost: one ISO for every PC. Current Nvidia cards get the Nvidia system
# after the install by themselves; older ones stay on the open driver, so say
# that games will run slowly on them before anything is installed.
nvidia_hardware_helper() {
    local support
    support=$(/usr/libexec/bazzite-detect-nvidia-support-status 2>/dev/null) || return 0
    [[ -z "$support" || "$support" == supported ]] && return 0
    local title="Older Nvidia graphics card"
    local text="<b>Your Nvidia graphics card is older than RTX / GTX 16.</b>

Nvidia's current driver doesn't support it, so RimFrost OS runs it with the
open driver. The desktop works, but games will run slowly.

$(lspci -nn | grep '\[03' | sed 's/^[^ ]* //')"
    local install_button="Install anyway" off_button="Turn off the PC"
    if [[ "${LC_MESSAGES:-${LANG:-C}}" == da* ]]; then
        title="Ældre Nvidia-grafikkort"
        text="<b>Dit Nvidia-grafikkort er ældre end RTX / GTX 16.</b>

Nvidias nuværende driver understøtter det ikke, så RimFrost OS bruger den
åbne driver. Skrivebordet virker, men spil kører langsomt.

$(lspci -nn | grep '\[03' | sed 's/^[^ ]* //')"
        install_button="Installer alligevel" off_button="Sluk PC'en"
    fi
    yad --warning --on-top --center --buttons-layout=center --text-align=center \
        --title="$title" --text="$text" \
        --button="$install_button:0" --button="$off_button:1"
    if [[ $? -eq 1 ]]; then
        systemctl poweroff || true
        exit 0
    fi
}
block_low_memory_install
efi="c12a7328-f81f-11d2-ba4b-00a0c93ec93b"
declare -A mount
while read -r device path; do
    mount["$device"]="-o bind,ro $path"
done < <(lsblk -o PATH,MOUNTPOINTS -nQ 'PARTTYPE=="'$efi'" && MOUNTPOINTS' 2> /dev/null || true)

for device in $(lsblk -o PATH -nQ 'PARTTYPE=="'$efi'" && !MOUNTPOINTS' 2> /dev/null || true); do
    mount["$device"]="-o ro -t vfat $device"
done

export mnt=$(mktemp -d)
trap "rmdir '$mnt'" EXIT

for device in "${!mount[@]}"; do
    export device
    export BAZZITE_INSTALLER_LOCALE="${LC_MESSAGES:-${LANG:-C}}"
    msg=$(sudo -E unshare -m sh -c '
        mount '"${mount[$device]} '$mnt'"' 2> /dev/null || exit 0
        shopt -s nullglob nocaseglob
        for dir in "$mnt"/EFI/*; do
            [ -d "$dir" ] || continue
            base=$(basename "$dir" | tr "[:upper:]" "[:lower:]")
            [[ "$base" == "fedora" || "$base" == "boot" || "$base" == "rimfrostsetup" ]] && continue
            grub=("$dir"/grub*.efi)
            (( ! ${#grub[@]} )) && continue
            if [[ "$BAZZITE_INSTALLER_LOCALE" == sv* ]]; then
                echo "GRUB-starthanteraren verkar vara installerad på $device vid ${dir#$mnt}\nRimFrost OS <a href=\"http://127.0.0.1:1290/General/Installation_Guide/troubleshoot_guide/#error-code-1\">har inte stöd för dubbelstart med någon annan Linux-installation.</a>\nInstallationer på denna disk som försöker återanvända EFI-partitionen kommer att misslyckas.\nInstallera antingen RimFrost OS på en annan disk eller ta bort denna partition eller starthanterare.\n\nSe <a href=\"http://127.0.0.1:1290/General/Installation_Guide/troubleshoot_guide/#how-to-remove-an-orphaned-copy-of-grub\">dokumentationen</a> för anvisningar.\n"
            else
                echo "The GRUB bootloader seems to be installed on $device at ${dir#$mnt}\nRimFrost OS <a href=\"http://127.0.0.1:1290/General/Installation_Guide/troubleshoot_guide/#error-code-1\">does not support dual boot with any other Linux installation.</a> \nInstalls to this disk that attempt to reuse this EFI partition will fail.\nEither RimFrost OS must be installed to a different disk, or this partition or boot loader must be removed.\n\nPlease see the <a href=\"http://127.0.0.1:1290/General/Installation_Guide/troubleshoot_guide/#how-to-remove-an-orphaned-copy-of-grub\">documentation</a> for instructions.\n"
            fi
        done
    ' || true)
    [ "$msg" ] || continue
    serve_docs
    if is_swedish_locale; then
        yad --image=dialog-warning --button=OK --buttons-layout=center --title="Befintlig Linux-starthanterare identifierad" --text="$msg"
    else
        yad --image=dialog-warning --button=OK --buttons-layout=center --title="Existing Linux bootloader detected" --text="$msg"
    fi
done


nvidia_hardware_helper
# The USB stick is only for installing: start the installer straight away
liveinst &
disown $!
