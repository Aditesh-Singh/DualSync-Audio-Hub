# 🎧 DualSync Audio Hub

> **Two Devices. One Sound.**

DualSync Audio Hub is a Windows desktop application that enables simultaneous audio playback through two independent audio devices.

It is designed to make it possible to listen to the same system audio through two separate Bluetooth/audio output devices at the same time.

---

✨ Features

- 🎧 Simultaneous audio playback on two independent devices
- 🔊 Bluetooth audio device support
- 🔄 Real-time audio routing
- ⚡ Stable audio synchronization
- 🎛️ Simple and user-friendly control interface
- 🖥️ Windows desktop application

---

🧠 How It Works

DualSync captures the system audio using the Windows audio system and processes the captured audio through the application's audio engine.

The audio is then routed to two separate output devices simultaneously.


             Windows System Audio
                     │
                     ▼
              WASAPI Capture
                     │
                     ▼
              Audio Processing
                     │
              ┌──────┴──────┐
              ▼             ▼
        Output Device 1  Output Device 2
              │             │
              ▼             ▼
          Earbud 1       Earbud 2


The application manages separate playback paths for the two output devices while attempting to keep playback stable and synchronized.

🛠️ Technology Stack

Technology	Purpose
C#	Application development
.NET 8	Application framework
WPF	Windows desktop UI
NAudio	Audio capture and playback
WASAPI	Windows audio capture/routing
Visual Studio	Development environment

💻 System Requirements
Windows 10 or later
.NET 8 compatible environment
Two available audio output devices
Bluetooth support for Bluetooth earbuds/headphones
Bluetooth/audio devices should be connected to Windows before starting playback

📥 Installation
Option 1 — Installer

Download the latest installer from the Releases section of this repository.

Run:

DualSync_Setup.exe
and follow the installation instructions.
Before using DualSync, make sure your desired audio output is set as the default Windows playback device.

Option 2 — Build from Source

Clone the repository:
git clone https://github.com/Aditesh-Singh/DualSync-Audio-Hub.git

Open:
DualSync.sln
in Visual Studio and build the project.

🚀 How to Use

Connect both Bluetooth/audio devices to Windows.
Start DualSync Audio Hub.
Select the required audio devices.
Start audio playback.
The same audio will be routed to both selected devices.
Use the application's controls to manage playback and audio output.


 📁 Project Structure

  
  ## 📁 Project Structure

```
DualSync-Audio-Hub/
│
├── Assets/
├── Audio/
├── Properties/
│   └── PublishProfiles/
│
├── App.xaml
├── App.xaml.cs
├── AssemblyInfo.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── DualSync.csproj
├── DualSync.sln
├── app.ico
├── .gitignore
└── README.md
```

⚠️ Limitations

Bluetooth audio devices can have different hardware buffers, clocks, codecs, and driver behavior.

Because of these differences, perfectly identical playback timing cannot always be guaranteed across independent Bluetooth devices.

DualSync focuses on providing stable simultaneous playback and minimizing noticeable synchronization differences.

🔮 Future Scope

Possible future improvements include:

Improved synchronization algorithms
More precise latency adjustment
Better device monitoring
Enhanced audio diagnostics
Additional audio device support
Improved recovery from device disconnection

👨‍💻 Contributors

Aditesh Singh — Developer
techmaster-cmd — Development & Testing

📄 License

This project is intended for educational and demonstration purposes.

⭐ Project
If you find this project useful, consider giving the repository a ⭐ on GitHub.
