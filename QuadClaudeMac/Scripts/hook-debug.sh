#!/bin/bash
# Debug wrapper for hook commands
echo "$(date): QUAD_INDEX=$QUAD_INDEX CMD=$@" >> /tmp/quadclaude-hook.log
echo "$(date): PATH=$PATH" >> /tmp/quadclaude-hook.log
/usr/local/bin/quadclaude "$@" 2>> /tmp/quadclaude-hook.log
echo "$(date): exit=$?" >> /tmp/quadclaude-hook.log
