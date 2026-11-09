#!/usr/bin/env bash
set -euo pipefail

status=0

fail_if_found() {
  local description="$1"
  local pattern="$2"
  local path="$3"
  shift 3

  if grep -RInE "$pattern" "$path" --include='*.cs' "$@"; then
    echo "::error::P2O guard: $description"
    status=1
  fi
}

# Клиент обязан оставаться P2O: отдавать файловые байты запрещено.
fail_if_found \
  "найден builder отдачи файловых сообщений" \
  'Build(Piece|Unchoke|Have|HaveAll|Bitfield)\b' \
  'SAS-Torrent.Core'

# Сырой отправки произвольных байтов в peer-путях быть не должно.
fail_if_found \
  "найден сырой SendAsync в peer-путях" \
  'SendAsync' \
  'SAS-Torrent.Core/Peers'

# Piece/Have/Bitfield/Unchoke разрешены только входящему парсеру и enum.
fail_if_found \
  "исходящее использование загрузочного сообщения вне входящего парсера" \
  'PeerMessageId\.(Piece|Unchoke|Have|Bitfield)' \
  'SAS-Torrent.Core/Peers' \
  --exclude='PeerConnection.cs' \
  --exclude='PeerWire.cs'

if [ "$status" -eq 0 ]; then
  echo "P2O guard: OK"
fi

exit "$status"
