#!/usr/bin/env bash
set -euo pipefail

repo_url="${RPACK_REPO_URL:-https://github.com/ppotepa/rpack.git}"
branch="${RPACK_BRANCH:-main}"
source_dir="${RPACK_SOURCE_DIR:-$HOME/.local/share/rpack/source}"
publish_dir="${RPACK_PUBLISH_DIR:-$HOME/.local/share/rpack/publish}"
bin_dir="${RPACK_BIN_DIR:-$HOME/.local/bin}"
dotnet_dir="${RPACK_DOTNET_DIR:-$HOME/.dotnet}"
dotnet_channel="${RPACK_DOTNET_CHANNEL:-10.0}"
mime_type="application/x-rpack-package"
install_binfmt="${RPACK_INSTALL_BINFMT:-auto}"
binfmt_name="${RPACK_BINFMT_NAME:-rpack}"
binfmt_wrapper="${RPACK_BINFMT_WRAPPER:-/usr/local/bin/rpack-exec}"
binfmt_conf="${RPACK_BINFMT_CONF:-/etc/binfmt.d/rpack.conf}"

log() {
    printf 'rpack installer: %s\n' "$*"
}

fail() {
    printf 'rpack installer: %s\n' "$*" >&2
    exit 1
}

has_command() {
    command -v "$1" >/dev/null 2>&1
}

run_as_root() {
    if [ "$(id -u)" -eq 0 ]; then
        "$@"
        return
    fi

    if has_command sudo; then
        sudo "$@"
        return
    fi

    return 1
}

write_root_string() {
    local value="$1"
    local target="$2"

    if [ "$(id -u)" -eq 0 ]; then
        printf '%s\n' "$value" > "$target"
        return
    fi

    printf '%s\n' "$value" | sudo tee "$target" >/dev/null
}

download() {
    local url="$1"
    local output="$2"

    if has_command curl; then
        curl -fsSL "$url" -o "$output"
        return
    fi

    if has_command wget; then
        wget -qO "$output" "$url"
        return
    fi

    fail "curl or wget is required."
}

ensure_git() {
    if ! has_command git; then
        fail "git is required. Install git with your distribution package manager and rerun this script."
    fi
}

dotnet_has_required_sdk() {
    has_command dotnet && dotnet --list-sdks 2>/dev/null | grep -Eq "^${dotnet_channel}[.]"
}

ensure_dotnet() {
    if [ -x "$dotnet_dir/dotnet" ]; then
        export DOTNET_ROOT="$dotnet_dir"
        export PATH="$dotnet_dir:$PATH"
    fi

    if dotnet_has_required_sdk; then
        return
    fi

    log ".NET SDK ${dotnet_channel} not found; installing user-local SDK to ${dotnet_dir}"
    mkdir -p "$dotnet_dir"

    local installer
    installer="$(mktemp)"
    download "https://dot.net/v1/dotnet-install.sh" "$installer"
    bash "$installer" --channel "$dotnet_channel" --install-dir "$dotnet_dir"
    rm -f "$installer"

    export DOTNET_ROOT="$dotnet_dir"
    export PATH="$dotnet_dir:$PATH"

    dotnet_has_required_sdk || fail "failed to install .NET SDK ${dotnet_channel}."
}

sync_source() {
    mkdir -p "$(dirname "$source_dir")"

    if [ -d "$source_dir/.git" ]; then
        log "updating ${source_dir}"
        git -C "$source_dir" fetch origin "$branch"
        git -C "$source_dir" checkout "$branch"
        git -C "$source_dir" pull --ff-only origin "$branch"
        return
    fi

    if [ -e "$source_dir" ]; then
        fail "${source_dir} exists but is not a git repository. Set RPACK_SOURCE_DIR or remove that directory."
    fi

    log "cloning ${repo_url} into ${source_dir}"
    git clone --branch "$branch" "$repo_url" "$source_dir"
}

publish_rpack() {
    log "publishing rpack CLI"
    rm -rf "$publish_dir"
    mkdir -p "$publish_dir"

    dotnet publish "$source_dir/src/Rpack.Cli/Rpack.Cli.csproj" \
        --configuration Release \
        --self-contained false \
        --output "$publish_dir"
}

install_launcher() {
    mkdir -p "$bin_dir"

    cat > "$bin_dir/rpack" <<EOF
#!/usr/bin/env bash
if [ -x "${dotnet_dir}/dotnet" ]; then
    export DOTNET_ROOT="${dotnet_dir}"
    export PATH="${dotnet_dir}:\$PATH"
fi
exec dotnet "${publish_dir}/rpack.dll" "\$@"
EOF

    chmod +x "$bin_dir/rpack"

    cat > "$bin_dir/rpack-open" <<EOF
#!/usr/bin/env bash
exec "${bin_dir}/rpack" open "\$@"
EOF

    chmod +x "$bin_dir/rpack-open"
}

add_path_to_profile_file() {
    local profile_file="$1"
    local marker_begin="# >>> rpack installer >>>"
    local marker_end="# <<< rpack installer <<<"

    mkdir -p "$(dirname "$profile_file")"
    touch "$profile_file"

    if grep -Fq "$marker_begin" "$profile_file"; then
        return
    fi

    cat >> "$profile_file" <<EOF

${marker_begin}
if [ -d "${bin_dir}" ]; then
    case ":\$PATH:" in
        *":${bin_dir}:"*) ;;
        *) export PATH="${bin_dir}:\$PATH" ;;
    esac
fi
${marker_end}
EOF
}

install_path_integration() {
    add_path_to_profile_file "$HOME/.profile"

    if [ -n "${SHELL:-}" ]; then
        case "$(basename "$SHELL")" in
            bash) add_path_to_profile_file "$HOME/.bashrc" ;;
            zsh) add_path_to_profile_file "$HOME/.zshrc" ;;
        esac
    fi

    [ -f "$HOME/.bashrc" ] && add_path_to_profile_file "$HOME/.bashrc"
    [ -f "$HOME/.zshrc" ] && add_path_to_profile_file "$HOME/.zshrc"

    case ":$PATH:" in
        *":$bin_dir:"*) ;;
        *) export PATH="$bin_dir:$PATH" ;;
    esac
}

install_file_association() {
    if ! has_command xdg-mime; then
        log "xdg-mime not found; skipping .rpack file association."
        return
    fi

    local applications_dir="$HOME/.local/share/applications"
    local mime_dir="$HOME/.local/share/mime/packages"
    local desktop_file="$applications_dir/rpack-open.desktop"
    local mime_file="$mime_dir/rpack.xml"

    mkdir -p "$applications_dir" "$mime_dir"

    cat > "$mime_file" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="${mime_type}">
    <comment>rpack package</comment>
    <glob pattern="*.rpack"/>
  </mime-type>
</mime-info>
EOF

    cat > "$desktop_file" <<EOF
[Desktop Entry]
Type=Application
Name=rpack package opener
Comment=Inspect, check, and apply rpack packages
Exec=${bin_dir}/rpack-open %f
Terminal=true
MimeType=${mime_type};
NoDisplay=true
Categories=Development;
EOF

    chmod +x "$desktop_file"

    if has_command update-mime-database; then
        update-mime-database "$HOME/.local/share/mime" >/dev/null 2>&1 || true
    fi

    if has_command update-desktop-database; then
        update-desktop-database "$applications_dir" >/dev/null 2>&1 || true
    fi

    xdg-mime default rpack-open.desktop "$mime_type" || log "could not set default .rpack association."
}

install_binfmt_association() {
    case "$install_binfmt" in
        0|false|False|FALSE|no|No|NO)
            log "binfmt_misc .rpack association disabled by RPACK_INSTALL_BINFMT=${install_binfmt}."
            return
            ;;
    esac

    if [ "$(uname -s)" != "Linux" ]; then
        log "binfmt_misc is Linux-only; skipping executable .rpack association."
        return
    fi

    if [ ! -d /proc/sys/fs/binfmt_misc ]; then
        log "binfmt_misc filesystem is not available; skipping executable .rpack association."
        return
    fi

    if [ "$(id -u)" -ne 0 ] && ! has_command sudo; then
        if [ "$install_binfmt" = "auto" ]; then
            log "sudo not found; skipping executable .rpack association."
            return
        fi

        fail "sudo is required to install binfmt_misc association."
    fi

    log "installing executable .rpack association through binfmt_misc"

    local wrapper_tmp
    local conf_tmp
    wrapper_tmp="$(mktemp)"
    conf_tmp="$(mktemp)"

    cat > "$wrapper_tmp" <<EOF
#!/usr/bin/env bash
set -euo pipefail

pkg="\${1:-}"
if [ -z "\$pkg" ]; then
    echo "usage: ./package.rpack [target_repo] [rpack-open-args...]" >&2
    exit 2
fi

shift || true

rpack_bin=""
if command -v rpack >/dev/null 2>&1; then
    rpack_bin="\$(command -v rpack)"
elif [ -x "${bin_dir}/rpack" ]; then
    rpack_bin="${bin_dir}/rpack"
else
    echo "rpack not found in PATH" >&2
    exit 127
fi

target="\${RPACK_TARGET_REPO:-\$PWD}"
if [ "\$#" -gt 0 ] && [[ "\${1:-}" != --* ]]; then
    target="\$1"
    shift || true
fi

exec "\$rpack_bin" open "\$pkg" "\$target" "\$@"
EOF

    cat > "$conf_tmp" <<EOF
:${binfmt_name}:E::rpack::${binfmt_wrapper}:
EOF

    if ! run_as_root install -m 0755 "$wrapper_tmp" "$binfmt_wrapper"; then
        rm -f "$wrapper_tmp" "$conf_tmp"
        if [ "$install_binfmt" = "auto" ]; then
            log "could not install ${binfmt_wrapper}; skipping binfmt_misc association."
            return
        fi

        fail "could not install ${binfmt_wrapper}."
    fi

    run_as_root mkdir -p "$(dirname "$binfmt_conf")"
    run_as_root install -m 0644 "$conf_tmp" "$binfmt_conf"
    rm -f "$wrapper_tmp" "$conf_tmp"

    if has_command modprobe; then
        run_as_root modprobe binfmt_misc >/dev/null 2>&1 || true
    fi

    if [ ! -e /proc/sys/fs/binfmt_misc/register ]; then
        run_as_root mount -t binfmt_misc none /proc/sys/fs/binfmt_misc >/dev/null 2>&1 || true
    fi

    if [ -e "/proc/sys/fs/binfmt_misc/${binfmt_name}" ]; then
        write_root_string "-1" "/proc/sys/fs/binfmt_misc/${binfmt_name}" || true
    fi

    if [ -e /proc/sys/fs/binfmt_misc/register ]; then
        write_root_string ":${binfmt_name}:E::rpack::${binfmt_wrapper}:" /proc/sys/fs/binfmt_misc/register || true
    fi

    if has_command systemctl; then
        run_as_root systemctl restart systemd-binfmt >/dev/null 2>&1 || true
    fi

    log "registered executable .rpack files via ${binfmt_wrapper}"
}

main() {
    ensure_git
    ensure_dotnet
    sync_source
    publish_rpack
    install_launcher
    install_path_integration
    install_file_association
    install_binfmt_association

    log "installed $("$bin_dir/rpack" version 2>/dev/null || printf 'rpack')"
    log "binary: ${bin_dir}/rpack"
    log "opener: ${bin_dir}/rpack-open"

    case ":$PATH:" in
        *":$bin_dir:"*) ;;
        *) log "restart the shell or run: export PATH=\"${bin_dir}:\$PATH\"" ;;
    esac
}

main "$@"
