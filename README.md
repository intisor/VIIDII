# FutaMeetWeb

FutaMeetWeb is a real-time online meeting platform built for educational institutions, specifically designed to facilitate virtual classes between lecturers and students. The application leverages ASP.NET Core and SignalR to provide seamless video conferencing and chat functionality.

## Features

- **Session Management**: Create, join, and manage interactive online sessions
- **Real-time Communication**: Video conferencing with SignalR-powered WebRTC
- **Live Chat**: Text-based communication within sessions
- **Role-based Access Control**: Different interfaces for students, lecturers, and administrators
- **Session Administration**: Monitor active sessions and participants

## Technology Stack

- **Backend**: ASP.NET Core (.NET 9.0)
- **Real-time Communication**: SignalR
- **Frontend**: Razor Pages, JavaScript, Bootstrap
- **WebRTC Integration**: SimplePeer.js
- **State Management**: ASP.NET Core Session

## Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 or later (recommended)
- Modern web browser with WebRTC support

## Getting Started

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/intisor/FutaMeetWeb.git
   ```

2. Navigate to the project directory:
   ```bash
   cd FutaMeetWeb
   ```

3. Restore dependencies:
   ```bash
   dotnet restore
   ```

4. Build the project:
   ```bash
   dotnet build
   ```

5. Run the application:
   ```bash
   dotnet run
   ```

6. Access the application at `https://localhost:5001` or `http://localhost:5000`

### Development Setup

1. Open the solution file `FutaMeetWeb.sln` in Visual Studio
2. Make sure all NuGet packages are restored
3. Set the startup project to `FutaMeetWeb`
4. Press F5 to start debugging

## Usage

### Authentication

For demo purposes, the application has pre-configured user accounts:
- Lecturer: Matric No: `Lec001`
- Student: Matric No: `123456` or `654321`
- Administrator: Matric No: `Admin`

### Creating a Session (Lecturers)

1. Log in with lecturer credentials
2. Navigate to "Create Session"
3. Enter a session title and create the session
4. Share the generated session ID with students

### Joining a Session (Students)

1. Log in with student credentials
2. Navigate to "Join Session"
3. Select an available session from the dropdown or enter a session ID
4. Participate in the video conference and chat

### Administration

1. Log in with admin credentials
2. Navigate to "Admin" to view active sessions and participants

## Project Structure

- `/Hubs`: SignalR hubs for real-time communication
- `/Models`: Application data models
- `/Pages`: Razor Pages for UI
- `/Services`: Business logic and service classes
- `/wwwroot`: Static files (CSS, JS, client libraries)

## Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- Federal University of Technology, Akure (FUTA)
- SignalR and WebRTC for enabling real-time communication capabilities
- Bootstrap for responsive UI components
