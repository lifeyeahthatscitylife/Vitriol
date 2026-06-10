Project Vitriol



**Overview**



Vitriol is a WPF-based editor and runtime system linked through shared DTOs. The system allows users to create tile-based maps, define collision layers, configure event triggers, and load maps into a runtime environment with player movement and camera-based viewport rendering.



**System Requirements**



* Window 10 or Windows 11
* .NET 8.0 SDK installed
* Ability to build via CMD (recommended) or Visual Studio 2022
* 8GB RAM minimum



**Building the Project**



Method 1 - CMD



1. Open CMD (Command Prompt)
2. type: cd "<insert file path to Vitriol root folder>"
3. To run editor, paste: dotnet run --project Vitriol.Editor\\Vitriol.Editor.csproj
4. To run runtime, paste: dotnet run --project Vitriol.Runtime\\Vitriol.Runtime.csproj
5. You can only have one open at a given time. Close the current WPF Application before trying to open the second.



Method 2 - Visual Studio 



1. Open the solution file (.sln)
2. Build the solution
3. Set either: Vitriol.Editor, or Vitriol.Runtime, as the startup project
4. Press F5 to run



**Using the Editor**



1. Create a new map with defined dimensions
2. Paint tiles using loaded tilesets
3. Define collision tiles
4. Add event triggers (NPC, Trigger, Warp)
5. Save the map
6. Launch runtime and load the saved map



**Notes**



* Autosave activates only after the first manual save for new maps
* All maps are stored in JSON format with versioning
* The runtime uses viewport-only rendering for performance optimisation
