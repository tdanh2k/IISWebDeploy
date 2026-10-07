#!/bin/sh
set -eu

project_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

# Assert the only cleanup targets are the project's own bin and obj directories.
[ "$(CDPATH= cd -- "$project_dir" && pwd)/bin" = "$project_dir/bin" ]
[ "$(CDPATH= cd -- "$project_dir" && pwd)/obj" = "$project_dir/obj" ]

rm -rf -- "$project_dir/bin" "$project_dir/obj"
dotnet publish "$project_dir/IISWebDeploy.csproj" -p:PublishProfile=Properties/PublishProfiles/FolderProfile.pubxml "$@"
