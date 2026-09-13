# DualSync 🎧

> Play the same system audio simultaneously through two independent
> audio output devices.

## 📌 Overview

DualSync is a Windows desktop application that allows the same system
audio to be played simultaneously through two separate audio output
devices.

It is designed for situations where two people want to listen to the
same movie, video, music, or other system audio using separate Bluetooth
earbuds or headphones.

DualSync captures system audio, buffers it, and sends the audio stream
to two independent output pipelines at the same time.

## ✨ Features

-   🎧 Dual audio output
-   🔊 Simultaneous playback on two independent devices
-   🎚️ Independent volume control
-   🔇 Individual device mute control
-   🔄 Device disconnect/reconnect handling
-   ⚡ Low-latency audio streaming
-   🎵 System audio capture using WASAPI Loopback
-   🖥️ Simple Windows desktop interface
-   🛡️ No external API keys or online services required

## ⚙️ How It Works

``` text
System Audio
      │
      ▼
WASAPI Loopback Capture
      │
      ▼
Audio Buffer
      │
      ├──────────────► Output Pipeline 1 ──► Device 1
      │
      └──────────────► Output Pipeline 2 ──► Device 2
```

The application captures the audio currently being played by Windows and
distributes the captured audio stream to two separate output pipelines.

Each pipeline sends the audio to its selected output device.

## 🛠️ Tech Stack

  Technology        Purpose
  ----------------- ----------------------------
  C#                Application development
  .NET 8            Application framework
  WPF               Windows desktop UI
  NAudio 2.2.1      Audio capture and playback
  WASAPI Loopback   System audio capture
  Visual Studio     Development environment

## 💻 Requirements

-   Windows 10 or Windows 11
-   .NET 8 compatible environment
-   Two available audio output devices
-   Bluetooth earbuds/headphones or other supported audio devices
-   Visual Studio 2022 (for building from source)

## 🚀 Installation

### Option 1 --- Run the Release Build

Download the latest release from the GitHub Releases section.

Extract the downloaded archive and launch:

``` text
DualSync.exe
```

### Option 2 --- Build From Source

Clone the repository:

``` bash
git clone <repository-url>
```

Open the solution:

``` text
DualSync.sln
```

Build the project using Visual Studio and run the application.

## 🎮 Usage

1.  Connect two audio devices to your Windows PC.
2.  Launch **DualSync**.
3.  Select the first output device.
4.  Select the second output device.
5.  Adjust the volume for each device if required.
6.  Start DualSync.
7.  Play any audio/video on your computer.
8.  The same audio will be played through both selected devices.

## 🖼️ Screenshots

Screenshots will be added to the `assets/` folder as part of the final
demo/release assets.

Recommended screenshots:

-   Main interface
-   Device selection
-   Dual audio playback
-   Independent volume/mute controls

## ⚠️ Known Limitations

DualSync depends on the behavior of the connected audio hardware and
Windows audio system.

-   Bluetooth devices may have different internal audio latency.
-   Synchronization can vary depending on Bluetooth hardware and
    drivers.
-   Windows audio configuration can affect playback behavior.
-   Different earbuds/headphones may introduce different amounts of
    latency.
-   Bluetooth connection quality can affect audio stability.
-   Wired and Bluetooth devices may behave differently.

Perfect synchronization cannot always be guaranteed across different
Bluetooth hardware because the final audio latency is partly controlled
by the devices themselves.

## 🔮 Future Improvements

Possible future improvements include:

-   Automatic latency calibration
-   Manual per-device delay adjustment
-   Improved synchronization algorithms
-   More advanced audio controls
-   Additional output device support
-   Audio device profiles
-   Improved device monitoring
-   Portable standalone release
-   Improved UI customization

## 📁 Project Structure

``` text
DualSync/
│
├── Audio/
│   ├── AudioCapture.cs
│   ├── AudioOutput.cs
│   └── DualAudioEngine.cs
│
├── App.xaml
├── App.xaml.cs
├── AssemblyInfo.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── DualSync.csproj
├── DualSync.sln
├── .gitignore
└── README.md
```

## 🔒 Privacy & Security

DualSync does not require external API keys, passwords, or online
authentication.

Audio processing is performed locally on the Windows machine.

No external cloud service is required for the core audio functionality.

## 📜 License

This project is open source.

See the `LICENSE` file for license details.

Third-party libraries used by this project remain subject to their
respective licenses.

## 👨‍💻 Project

**DualSync**

**Version:** 1.0.0

A Windows application for simultaneous audio playback through two
independent output devices.

## ⭐ Support

If you find DualSync useful, consider giving the repository a ⭐ on
GitHub.
