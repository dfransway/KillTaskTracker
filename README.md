# KillTaskTracker
A Decal plugin for Asheron's Call that automatically and persistently tracks kill tasks.

This plugin has no GUI and is entirely automated. It detects chat messages to start or end the task, and the kill messages themselves to update the counts. To view your tasks and progress in each type '/tasks'.

Your progress is saved in a file named after the character in question in the directory where you place the DLL.

The kill tasks are defined in 'task_definitions.json' which must be colocated with the DLL. Please send me a message if there is something I am missing, or that isn't working correctly.
