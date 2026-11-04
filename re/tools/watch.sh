#!/bin/bash
# watch.sh <fm pid> <addr hex> <len hex> <log file>
# Read only. Logs every change of <len> bytes at <addr> until "<log file>.stop" exists.
# Use it while stepping through an FMRTE dropdown (Down, Ctrl+S, zoom on the name): the n-th
# logged value belongs to the n-th name you saw. Needs the Tools build copied to %TEMP%\fm27tools.
cd "$TEMP" || exit 1
export FM_PID="$1"; addr="$2"; len="$3"; out="$4"; last=""
rm -f "$out.stop"; : > "$out"
while [ ! -f "$out.stop" ]; do
  v=$(./fm27tools/Fm27Editor.Tools.exe hex "$addr" "$len" 2>/dev/null | awk '{s=s $2} END {print s}')
  if [ -n "$v" ] && [ "$v" != "$last" ]; then echo "$v" >> "$out"; last="$v"; fi
done
