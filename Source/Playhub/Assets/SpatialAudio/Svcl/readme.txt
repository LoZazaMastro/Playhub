


SoundVolumeCommandLine v1.28
Copyright (c) 2021 - 2026 Nir Sofer
Web site: https://www.nirsoft.net/utils/sound_volume_command_line.html



Description
===========

SoundVolumeCommandLine (svcl.exe) is a console application that allows
you to do many actions related to sound volume from command-line,
including - set sound volume of devices and applications, mute/unmute
devices and applications, increase/decrease volume of devices and
applications, set the volume level of specific channel, set the default
render/capture device, get the current sound volume level of specific
device, and more...

svcl.exe is the console version of the SoundVolumeView tool, and you can
use all commands of SoundVolumeView in svcl.exe, with exactly the same
syntax. If you want to learn how the use the commands of this tool,
simply go to the 'Command-Line Options' section in the SoundVolumeView
Web page.

There is also a new command, /Stdout , available only in svcl.exe, which
sends information related to the executed command to stdout (see below).



System Requirements
===================

This utility works on Windows Vista, Windows 7, Windows 8, Windows 2008,
Windows 10, and Windows 11. Both 32-bit and 64-bit systems are supported.
Windows XP and older systems are not supported.



Versions History
================


* Version 1.28:
  o Added 'Cursor' option to all command-line options, which allows
    you to change the volume of the application under the mouse cursor.
  o Added 'CursorName' option to all command-line options, which
    allows you to change the volume of all instances of the application
    under the mouse cursor.

* Version 1.27:
  o Updated the /SetSpatial command to work on Windows 11 25H2.

* Version 1.26:
  o Added /SetBooleanFxProperty command-line option, which allows you
    to turn on/off different options of a device, like 'Loudness
    Equalization', 'Headphone Virtualization', 'Voice Cancellation', and
    others.

* Version 1.25:
  o Added /GetColumnValue command-line option, which allows to get a
    value of specific column for the specified item, for example:
    svcl.exe /GetColumnValue "Realtek High Definition
    Audio\Device\Speakers\Render" "DeviceState"

* Version 1.20:
  o Added /SaveDeviceFormat and /LoadDeviceFormat command-line
    options, which allows you to save the data of the device default
    format into a file, and then later load it back into the device.
  o You should use these new commands if you have a format that
    cannot be set with the /SetDefaultFormat command.

* Version 1.15:
  o Added /SetSpeakersConfig command-line option, which allows you to
    set the desired speakers configuration (Stereo, Quadraphonic, 5.1
    Surround, 7.1 Surround) from command-line.
  o Added /Columns command-line option, which allows you to set the
    columns to display or the columns to export from command-line, for
    example:
    svcl.exe /scomma "c:\temp\sound-items.csv" /Columns
    "Name,Command-Line Friendly ID,Volume Percent"

* Version 1.10:
  o You can now use wildcard with the command-line friendly ID, for
    example:
    svcl.exe /Switch "Realtek*\Device\Speakers\Render"
  o You can now specify the number of channels in the
    /SetDefaultFormat command, for example:
    svcl.exe /SetDefaultFormat "Speakers" 24 48000 4

* Version 1.05:
  o Updated the /SetAppDefault and /SetSpatial commands to work on
    Windows 11.
  o You can now use any variable inside the .cfg file of
    SoundVolumeView tool (SoundVolumeView.cfg) in order to set the
    configuration from command line, for example:
    svcl.exe /SaveFileEncoding 3 /ShowUnpluggedDevices 1
    /ShowDisabledDevices 1

* Version 1.00 - First release.



The /Stdout command-line option
===============================

By default, when you use a command to get the current sound volume or
mute state, the value is returned through the return code of the .exe
file and you can get it with the %errorlevel% variable, for example:
svcl.exe /GetPercent "Realtek High Definition
Audio\Device\Speakers\Render"
echo %errorlevel%

With svcl.exe, there is another option, you can add the /Stdout command,
and then the value will be send to stdout and it'll be displayed inside
your command prompt window, for example:
svcl.exe /Stdout /GetPercent "Realtek High Definition
Audio\Device\Speakers\Render"

If you add the /Stdout command to a set command ( /SetVolume, /SetDefault
, /Mute , /ChangeVolume , and others....) , the list of the found sound
items will be sent to Stdout, so you can see which sound items are
affected by the executed command.



Command-Line Examples
=====================


* Set the volume of Chrome Web browser to 50%:
  svcl.exe /SetVolume "chrome.exe" 50
* Mute the volume of Firefox Web browser:
  svcl.exe /Mute "Firefox.exe"
* Unmute the 'Speakers' device:
  svcl.exe /Unmute "Speakers"
* Mute the 'System Sounds' of Windows operating system:
  svcl.exe /Mute "System Sounds"
* Switch the microphone between mute and unmute state:
  svcl.exe /Switch "Microphone"
* Switch the sound of VLC Player application between mute and unmute
  state:
  svcl.exe /Switch "vlc.exe"
* Switch your default render device between Mute and Unmute state:
  svcl.exe /Switch "DefaultRenderDevice"
* Decrease the volume of Speakers device by 5% :
  svcl.exe /ChangeVolume "Speakers" -5
* Set the device that its ID is 'Realtek High Definition
  Audio\Device\Speakers\Render' as the default render device. (You can
  find the Device ID in SoundVolumeView tool - 'Command-Line Friendly ID'
  or 'Item ID' column)
  svcl.exe /SetDefault "Realtek High Definition
  Audio\Device\Speakers\Render" all
* Switch the default render device, between the 2 specified devices.
  (You can find the Device ID in SoundVolumeView tool - 'Command-Line
  Friendly ID' or 'Item ID' column)
  svcl.exe /SwitchDefault
  "{0.0.0.00000000}.{a77a09b2-1ec6-49c3-860a-68945904a2f1}"
  "{0.0.0.00000000}.{7747b192-73b2-47d3-a2c0-168e94af7f9e}" all
* Set the default render device only for Chrome Web browser (Works only
  starting from Windows 10 April 2018 Update)
  svcl.exe /SetAppDefault "High Definition Audio
  Device\Device\Speakers\Render" all "chrome.exe"
* Set the volume level of all opened applications to 100%:
  svcl.exe /SetVolume AllAppVolume 100
* Set the left channel to 50% on Chrome Web browser.
  svcl.exe /SetVolumeChannels "chrome.exe" 50 100
* Sets the 'Spatial sound format' of the specified sound device on
  Windows 10.
  svcl.exe /SetSpatial "High Definition Audio
  Device\Device\Speakers\Render" "Windows Sonic For Headphones"
* Get the current volume level (in percent) of 'Speakers' device and
  sent it to stdout.
  svcl.exe /Stdout /GetPercent "Speakers"
* Write the list of all active sound items to stdout, in
  comma-delimited format.
  svcl.exe /scomma ""



License
=======

This utility is released as freeware. You are allowed to freely
distribute this utility via CD-ROM, DVD, Internet, or in any other way,
as long as you don't charge anything for this and you don't sell it or
distribute it as a part of commercial product. If you distribute this
utility, you must include all files in the distribution package, without
any modification !



Disclaimer
==========

The software is provided "AS IS" without any warranty, either expressed
or implied, including, but not limited to, the implied warranties of
merchantability and fitness for a particular purpose. The author will not
be liable for any special, incidental, consequential or indirect damages
due to loss of data or any other reason.



Feedback
========

If you have any problem, suggestion, comment, or you found a bug in my
utility, you can send a message to support@nirsoft.net
