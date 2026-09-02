Claude Code Notification Sound Setup
=====================================

Paste the instructions below into Claude Code and it will set everything up for you.


STEP 1: Extract this folder somewhere on your computer.

STEP 2: Open Claude Code and paste this:

------- COPY EVERYTHING BELOW THIS LINE -------

I need you to set up notification sounds so I know when you're done responding and when you need my attention. There are two sound files in the folder with this README. Ask me where I extracted the folder so you know the source path.

Here's what to do:

1. Create the directory ~/.claude/sounds/ if it doesn't exist:
   mkdir -p ~/.claude/sounds

2. Copy BOTH sound files from the extracted folder into that directory:
   - "Email-Notification-Inbox-Snap.wav" (plays when you finish responding)
   - "mixkit-dry-pop-up-notification-alert-2356.wav" (plays when you need my attention — permissions, idle, etc.)

3. Detect my OS and add hooks to my ~/.claude/settings.json using the right commands:

   On Mac, use afplay:
   - Stop sound: "afplay ~/.claude/sounds/Email-Notification-Inbox-Snap.wav"
   - Notification sound: "afplay ~/.claude/sounds/mixkit-dry-pop-up-notification-alert-2356.wav"

   On Windows, use PowerShell (replace USERNAME with my actual Windows username):
   - Stop sound: "powershell -WindowStyle Hidden -NonInteractive -Command \"(New-Object Media.SoundPlayer 'C:\\Users\\USERNAME\\.claude\\sounds\\Email-Notification-Inbox-Snap.wav').PlaySync()\""
   - Notification sound: "powershell -WindowStyle Hidden -NonInteractive -Command \"(New-Object Media.SoundPlayer 'C:\\Users\\USERNAME\\.claude\\sounds\\mixkit-dry-pop-up-notification-alert-2356.wav').PlaySync()\""

   The hook structure in settings.json should be:

   {
     "hooks": {
       "Stop": [
         {
           "hooks": [
             {
               "type": "command",
               "command": "THE STOP SOUND COMMAND",
               "async": true
             }
           ]
         }
       ],
       "Notification": [
         {
           "matcher": "permission_prompt",
           "hooks": [
             {
               "type": "command",
               "command": "THE NOTIFICATION SOUND COMMAND",
               "async": true
             }
           ]
         },
         {
           "matcher": "idle_prompt",
           "hooks": [
             {
               "type": "command",
               "command": "THE NOTIFICATION SOUND COMMAND",
               "async": true
             }
           ]
         }
       ]
     }
   }

   If I already have a settings.json with other stuff in it, merge the hooks in — don't overwrite the rest of my config.

4. Once both hooks are configured, play each sound one at a time for me:
   - First play "Email-Notification-Inbox-Snap.wav" and tell me "This is the sound that plays when I finish responding."
   - Then play "mixkit-dry-pop-up-notification-alert-2356.wav" and tell me "This is the sound that plays when I need your attention."
   - After both play, ask me: "How's the volume on those? If either one is too loud or too quiet, I can adjust it for you."

5. If I say the volume needs adjusting:
   - On Mac, you can control volume with afplay's --volume flag (0.0 to 1.0). Update the hook command to include it, e.g.: "afplay --volume 0.5 ~/.claude/sounds/Email-Notification-Inbox-Snap.wav"
   - On Windows, use PowerShell to adjust the system-level playback or use ffmpeg/sox if available. The simplest approach: use powershell to download and run a volume-adjusted copy via [Audio.SoundPlayer]. If sox or ffmpeg is installed, re-encode the wav at a different volume: "ffmpeg -i input.wav -filter:a volume=0.5 output.wav" and replace the file.
   - Play the adjusted sound again and ask if it's better. Keep adjusting until I'm happy.

------- STOP COPYING HERE -------


That's it! Claude will handle the rest.
