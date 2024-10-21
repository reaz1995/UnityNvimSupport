#!/bin/bash
SCRIPT_PATH=$1
WSL_DISTRO="Ubuntu"
SESSION_NAME="unity_dev"
wsl -d $WSL_DISTRO tmux has-session -t $SESSION_NAME 2>/dev/null

if [ $? != 0 ]; then
    wsl -d $WSL_DISTRO tmux new-session -d -s $SESSION_NAME
fi

if wsl -d $WSL_DISTRO tmux display-message -t $SESSION_NAME -p "#{pane_current_command}" | grep -q "lf" ; then
    wsl -d $WSL_DISTRO tmux send-keys -t $SESSION_NAME ":e | execute 'edit ' . system('wslpath \"$SCRIPT_PATH\"') " C-m
elif wsl -d $WSL_DISTRO tmux display-message -t $SESSION_NAME -p "#{pane_current_command}" | grep -q "nvim" ; then
    wsl -d $WSL_DISTRO tmux send-keys -t $SESSION_NAME ":e | execute 'edit ' . system('wslpath \"$SCRIPT_PATH\"') " C-m
else
    wsl -d $WSL_DISTRO tmux rename-window "nvim"
    wsl -d $WSL_DISTRO tmux send-keys -t $SESSION_NAME "wslpath \"$SCRIPT_PATH\" | xargs nvim " C-m
fi
