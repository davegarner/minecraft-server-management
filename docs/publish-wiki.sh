#!/usr/bin/env bash
# Publishes docs/wiki (and the Word user guide) to the repository's Gitea wiki.
#
# Usage: docs/publish-wiki.sh ["commit message"]
#
# The wiki repository is <origin>.wiki.git. Gitea only creates it once the first wiki page exists,
# so on a brand-new repository open the Wiki tab in Gitea and save any page first.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
origin="$(git -C "$root" remote get-url origin)"
wiki_url="${origin%.git}.wiki.git"
message="${1:-Update wiki from docs/wiki}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "Cloning $wiki_url"
if ! git clone --quiet "$wiki_url" "$work/wiki" 2>"$work/clone.log"; then
    cat "$work/clone.log" >&2
    if grep -q "initialize the wiki" "$work/clone.log"; then
        echo "The wiki doesn't exist yet: open the repository's Wiki tab in Gitea, create any page, then re-run this script." >&2
    fi
    exit 1
fi

cd "$work/wiki"
branch="$(git symbolic-ref --short HEAD 2>/dev/null || echo master)"

# Replace the wiki contents with docs/wiki, plus the generated Word guide for download.
git rm -rq --ignore-unmatch . >/dev/null
cp -R "$root/docs/wiki/." .
cp "$root/docs/Minecraft-Server-Manager-User-Guide.docx" .
git add -A

if git diff --cached --quiet; then
    echo "Wiki is already up to date."
    exit 0
fi
git commit --quiet -m "$message"
git push --quiet origin "HEAD:$branch"
echo "Published $(git ls-files '*.md' | wc -l | tr -d ' ') pages to ${origin%.git}/wiki"
