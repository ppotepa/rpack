#!/usr/bin/env bash
set -euo pipefail

repo_url="${RPACK_REPO_URL:-https://github.com/ppotepa/rpack.git}"
branch="${RPACK_BRANCH:-main}"
source_dir="${RPACK_SOURCE_DIR:-$HOME/.local/share/rpack/source}"
publish_dir="${RPACK_PUBLISH_DIR:-$HOME/.local/share/rpack/publish}"
bin_dir="${RPACK_BIN_DIR:-$HOME/.local/bin}"
dotnet_dir="${RPACK_DOTNET_DIR:-$HOME/.dotnet}"
dotnet_channel="${RPACK_DOTNET_CHANNEL:-10.0}"

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
}

main() {
    ensure_git
    ensure_dotnet
    sync_source
    publish_rpack
    install_launcher

    log "installed $("$bin_dir/rpack" version 2>/dev/null || printf 'rpack')"
    log "binary: ${bin_dir}/rpack"

    case ":$PATH:" in
        *":$bin_dir:"*) ;;
        *) log "add ${bin_dir} to PATH if 'rpack' is not found in a new shell." ;;
    esac
}

main "$@"
