#!/usr/bin/env bash

mkdir -p ./compiled/
# clean
rm -f ./compiled/*.teal

set -e # die on error

python ./compile.py "$1" ./build/approval.teal ./build/clear.teal
