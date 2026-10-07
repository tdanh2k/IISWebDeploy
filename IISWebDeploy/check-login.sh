#!/bin/sh
set -eu

base=${1:-http://localhost:5080}
cookie=$(mktemp)
page=$(mktemp)
logout_page=$(mktemp)
trap 'rm -f "$cookie" "$page" "$logout_page"' EXIT

status() { curl -k -sS "$@"; }

[ "$(status -o /dev/null -w '%{http_code}' "$base/login")" = 200 ]
[ "$(status -o "$page" -w '%{http_code}' -c "$cookie" "$base/login?ReturnUrl=%2F")" = 200 ]
token=$(sed -n 's/.*name="__RequestVerificationToken"[^>]*value="\([^"]*\)".*/\1/p' "$page" | head -n 1)
[ -n "$token" ]
[ "$(status -o /dev/null -w '%{http_code}' -b "$cookie" -X POST "$base/auth/login" --data 'password=Test%40123')" = 400 ]
[ "$(status -o /dev/null -w '%{http_code}' -b "$cookie" -X POST "$base/auth/login" --data-urlencode '__RequestVerificationToken=invalid' --data 'password=Test%40123')" = 400 ]
[ "$(status -o /dev/null -w '%{http_code}' -b "$cookie" -X POST "$base/auth/login" --data-urlencode "__RequestVerificationToken=$token" --data 'password=wrong')" = 302 ]
headers=$(status -D - -o /dev/null -b "$cookie" -c "$cookie" -X POST "$base/auth/login" --data-urlencode "__RequestVerificationToken=$token" --data 'password=Test%40123')
printf '%s\n' "$headers" | grep -qi '^set-cookie: iisdeploy.auth='
printf '%s\n' "$headers" | grep -qi '^set-cookie: iisdeploy.auth=.*httponly'
! printf '%s\n' "$headers" | grep -qi '^set-cookie: iisdeploy.auth=.*secure'
[ "$(status -o /dev/null -w '%{http_code}' -b "$cookie" "$base/")" = 200 ]
[ "$(status -o /dev/null -w '%{http_code}' -X POST "$base/login" --data 'password=Test%40123')" = 400 ]
[ "$(status -o "$logout_page" -w '%{http_code}' -b "$cookie" -c "$cookie" "$base/")" = 200 ]
logout_token=$(sed -n 's/.*name="__RequestVerificationToken"[^>]*value="\([^"]*\)".*/\1/p' "$logout_page" | head -n 1)
[ -n "$logout_token" ]
[ "$(status -o /dev/null -w '%{http_code}' -b "$cookie" -c "$cookie" -X POST "$base/logout" --data-urlencode "__RequestVerificationToken=$logout_token")" = 302 ]
[ "$(status -o /dev/null -w '%{http_code}' -b "$cookie" "$base/")" = 302 ]
printf '%s\n' 'login regression check passed'
