#!/bin/sh
set -eu

project_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
publish_dir="$project_dir/bin/Release/net10.0/publish"
profile="$project_dir/Properties/PublishProfiles/FolderProfile.pubxml"
clean_script="$project_dir/publish-clean.sh"

[ "$(CDPATH= cd -- "$project_dir" && pwd)/bin" = "$project_dir/bin" ]
[ "$(CDPATH= cd -- "$project_dir" && pwd)/obj" = "$project_dir/obj" ]
[ "$(CDPATH= cd -- "$project_dir/.." && pwd)/IISWebDeploy" = "$project_dir" ]
[ -f "$profile" ]
[ "$(sed -n 's#.*<PublishDir>\(.*\)</PublishDir>#\1#p' "$profile")" = '$(MSBuildProjectDirectory)/bin/Release/net10.0/publish/' ]
grep -Fq 'Name="CleanFolderPublishDirectory" BeforeTargets="PrepareForPublish"' "$profile"
grep -Fq "Condition=\"'\$(DeleteExistingFiles)' == 'true' And '\$(PublishDir)' == '\$(MSBuildProjectDirectory)/bin/Release/net10.0/publish/'\"" "$profile"
grep -Fq '<RemoveDir Directories="$(PublishDir)" />' "$profile"

cleanup_line=$(grep -n '^rm -rf -- "$project_dir/bin" "$project_dir/obj"$' "$clean_script" | cut -d: -f1)
publish_line=$(grep -n '^dotnet publish ' "$clean_script" | cut -d: -f1)
[ -n "$cleanup_line" ] && [ -n "$publish_line" ] && [ "$cleanup_line" -lt "$publish_line" ]

mkdir -p "$publish_dir"
sentinel="$publish_dir/.stale-cleanup-sentinel"
printf '%s\n' stale > "$sentinel"
dotnet publish "$project_dir/IISWebDeploy.csproj" -p:PublishProfile=Properties/PublishProfiles/FolderProfile.pubxml
[ ! -e "$sentinel" ]
[ -f "$publish_dir/IISWebDeploy.dll" ]
[ -f "$publish_dir/wwwroot/IISWebDeploy.styles.css" ]
[ -f "$publish_dir/wwwroot/_content/ShadCn.Blazor.Theme.Default/theme.css" ]
! find "$publish_dir/wwwroot" -iname '*bootstrap*' -print -quit | grep -q .
