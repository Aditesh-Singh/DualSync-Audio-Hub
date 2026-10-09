# 🎧 DualSync Audio Hub

> **Two Devices. One Sound.**

DualSync Audio Hub is a modern Windows desktop application that enables simultaneous audio playback across two independent audio devices (such as Bluetooth earbuds, wireless headphones, or external speakers).

It captures system audio in real-time, splits the stream into two independent playback pipelines, and provides precision delay buffering, volume controls, waveform analytics, and hearing safety monitoring.

---

## ✨ Features

- 🎧 **Dual Independent Output:** Broadcast identical Windows system audio to two separate endpoints simultaneously.
- 🔊 **Bluetooth & Multi-Endpoint Support:** Works seamlessly with Bluetooth earbuds, USB headsets, analog audio jacks, and external DACs.
- 🎚️ **Independent Volume & Mute:** Per-channel volume gain sliders (0–100%) and instant mute toggles for each listener.
- ⏱️ **Manual Audio Phase Delay:** Fine-tune circular FIFO buffer delay from -20.0 ms to +20.0 ms (with 0.1 ms precision) or Reset to Neutral (0.0 ms) to align Bluetooth hardware latency.
- 🔔 **Per-Node Test Chimes:** Send isolated synthetic frequency test chimes to Earbuds 1 (523 Hz) and Earbuds 2 (659 Hz) for rapid endpoint verification.
- 🛡️ **Hearing Safety Guardian:** Continuous exposure monitoring against an 80% volume threshold and a smart 120-minute (2-hour) listening break reminder popup.
- 📊 **Real-Time Oscilloscope:** Dual-channel visualizer rendering cyan (Primary) and purple (Secondary) audio waveforms with adjustable zoom (1.0x to 4.0x).
- 📋 **System Telemetry & Audit Report:** Real-time metrics matrix, hardware endpoint state logs, battery health, and one-click printable diagnostic summary export.
- ⚡ **Low-Latency WASAPI Core:** High-performance loopback capture without requiring virtual audio cables, external drivers, or cloud services.

---

## 🖥️ Application Interface Sections

DualSync Audio Hub is organized into four dedicated operational sections:

### 1. Audio — Dual Sync Console
- **Broadcast Controls:** One-click Start/Stop DualStream synchronization engine.
- **Node 1 & Node 2 Strips:** Hardware endpoint assignment, independent volume sliders, mute toggles, and synthetic test chime buttons.
- **Phase Delay Matrix:** Precision circular buffer offset slider (-20.0 ms to +20.0 ms) to manually balance inter-device Bluetooth latency, neutral reset button, and Audible Echo Test mode.
- **Live Stream Timer:** Real-time tracking of continuous streaming session duration.

### 2. Devices — Audio Device Management
- **Hardware Endpoint Detection:** Automatic enumeration and status monitoring of all connected WASAPI playback endpoints.
- **Node Assignment:** Primary and Secondary output selection menus with device refresh capabilities.
- **Host Laptop Profile:** Displays host machine identity, active power source (AC/Battery), battery life percentage, and default communication endpoint status.

### 3. Analytics — Sound Activity & Diagnostics
- **Dual Waveform Visualizer:** Real-time multi-channel oscilloscope rendering separate cyan and purple waveform traces directly driven by live playback stream telemetry.
- **Dynamic Zoom Control:** Interactive zoom slider scaling waveform visual resolution from 1.0x to 4.0x with linear tick rendering.
- **Diagnostic Trace Stream:** Live application event log capturing node gain adjustments, mute events, buffer state changes, and hardware connections.

### 4. Report — System Condition & Telemetry
- **Hearing Safety Card:** Visual exposure gauges for current volume and continuous listening duration (00:00:00 / 02:00:00) with color-coded safety badges.
- **Consolidated Telemetry Grid:** Formatted audit matrix logging hardware devices, sample format mixes, configured phase delay, and session durations.
- **Export & Print Diagnostic Summary:** Compiles and saves a comprehensive timestamped system audit log (`.txt`) with native Windows Print dialog integration.

---

## 🧠 System Architecture

DualSync captures Windows system audio via WASAPI Loopback, routes PCM samples through an adjustable circular FIFO delay buffer, applies independent per-channel volume multipliers, and delivers synchronized audio packets to both MMDevice endpoints.

```
                     Windows System Audio
                              │
                              ▼
                     WASAPI Loopback Capture
                     (AudioCapture.cs)
                              │
                              ▼
                   DualAudioEngine (Master Hub)
                              │
               ┌──────────────┴──────────────┐
               ▼                             ▼
       AudioOutput (Node 1)          AudioDelayBuffer (Node 2)
      [Independent Gain / Mute]      [FIFO Delay Offset: -20 to +20 ms]
               │                             │
               │                             ▼
               │                    AudioOutput (Node 2)
               │                    [Independent Gain / Mute]
               ▼                             ▼
       Earbuds 1 (Primary)          Earbuds 2 (Secondary)
```

> **Note on Latency Adjustment:** The Audio Phase Delay slider provides a *configured circular buffer timing offset* to compensate for differing Bluetooth hardware or codec delays. DualSync does not perform acoustic microphone-based round-trip measurements.

---

## 🛠️ Technology Stack

| Component | Technology | Description |
| :--- | :--- | :--- |
| **Language** | C# 12 | Core application logic and DSP handling |
| **Framework** | .NET 8 (Windows Desktop) | High-performance Windows application runtime |
| **UI Engine** | WPF & XAML | Modern dashboard interface with custom vector controls |
| **Audio Core** | NAudio 2.2.1 | WASAPI loopback capture and multi-device routing |
| **Installer** | Inno Setup 6 | Self-contained, single-executable Windows installer |
| **IDE** | Visual Studio 2022 | Primary development and build environment |

---

## 💻 System Requirements

- **Operating System:** Windows 10 (Build 19041+) or Windows 11 (x64)
- **Runtime:** Included in self-contained installer (.NET 8 runtime pre-bundled)
- **Hardware Endpoints:** Two functional audio output endpoints (Bluetooth, USB, or 3.5mm)
- **Audio Device Setup:** Both devices must be paired and connected in Windows Settings prior to starting DualSync broadcast.

---

## 📥 Installation

### Option 1 — Windows Setup Installer (Recommended)
1. Download the latest installer from the [Releases](https://github.com/Aditesh-Singh/DualSync-Audio-Hub/releases) page:
   ```
   DualSync_Audio_Hub_Setup.exe
   ```
2. Run the installer and follow the setup wizard.
3. The installer registers Desktop and Start Menu shortcuts and includes a clean Windows Uninstaller.

### Option 2 — Build from Source
1. Clone the repository:
   ```bash
   git clone https://github.com/Aditesh-Singh/DualSync-Audio-Hub.git
   ```
2. Navigate into the project folder:
   ```bash
   cd DualSync-Audio-Hub/DualSync-Audio-Hub
   ```
3. Build using the .NET CLI or Visual Studio:
   ```bash
   dotnet build DualSync.sln -c Release
   ```
4. Run the executable from `bin\Release\net8.0-windows\DualSync.exe`.

---

## 🚀 Usage Guide

1. **Connect Devices:** Ensure both pairs of earbuds or speakers are connected and listed in Windows Sound settings. Open **Settings → System → Sound → Output** and select your preferred device as the **Default Output Device**.
2. **Launch DualSync:** Open DualSync Audio Hub from the Start Menu or Desktop.
3. **Select Endpoints:** On the **Audio** or **Devices** tab, pick Earbuds 1 (Primary) and Earbuds 2 (Secondary).
4. **Start Streaming:** Click **Start DualSync**. System audio will now route through both endpoints.
5. **Adjust Sync:** If one Bluetooth device lags behind the other, adjust the **Audio Phase Delay** slider until both streams sound in sync.
6. **Hearing Safety:** DualSync will notify you after 120 continuous listening minutes with an interactive break reminder.

---

## 📁 Project Structure

```
DualSync-Audio-Hub/
├── .gitignore
├── README.md
│
└── DualSync-Audio-Hub/
    ├── Assets/
    │   ├── LOGO.png
    │   └── app.ico
    ├── Audio/
    │   ├── AudioCapture.cs
    │   ├── AudioDelayBuffer.cs
    │   ├── AudioLatencyCalibrator.cs
    │   ├── AudioOutput.cs
    │   └── DualAudioEngine.cs
    ├── Properties/
    │   └── PublishProfiles/
    │       └── FolderProfile.pubxml
    ├── App.xaml
    ├── App.xaml.cs
    ├── AssemblyInfo.cs
    ├── DualSync.csproj
    ├── DualSync.sln
    ├── MainWindow.xaml
    ├── MainWindow.xaml.cs
    ├── installer.iss
    ├── app.ico
    ├── .gitignore
    └── README.md
```

---

## ⚠️ Limitations & Considerations

- **Bluetooth Clock Drift:** Bluetooth chips use internal hardware clocks that may drift over extended sessions. DualSync provides manual phase delay adjustment to re-align timing as needed.
- **Codec Buffering:** High-latency Bluetooth codecs (such as SBC) introduce variable buffering delays depending on the PC's Bluetooth adapter and distance.
- **WASAPI Exclusive Mode:** DualSync captures shared system audio. Applications running in WASAPI Exclusive mode bypass Windows Loopback.

---

## 🔮 Future Scope

- Automated acoustic sync calibration using PC microphone test pulses.
- ASIO multi-device driver output support.
- Per-application audio routing and EQ profiles.
- Tray minimization with global hotkeys for volume and phase delay.

---

## 👨‍💻 Contributors

- **Aditesh Singh** — Developer ([@Aditesh-Singh](https://github.com/Aditesh-Singh))
- **techmaster-cmd** — Development & Testing

---

## 📄 License

This project is open source and intended for personal, educational, and demonstration purposes. See the repository for third-party library licenses (NAudio, Microsoft .NET).

---

⭐ If you find DualSync Audio Hub useful, consider starring the repository on GitHub!
