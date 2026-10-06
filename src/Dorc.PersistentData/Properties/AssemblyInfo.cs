using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
[assembly: AssemblyCulture("")]

// This project sets GenerateAssemblyInfo=False, so the <InternalsVisibleTo> MSBuild items in
// the .csproj are ignored; the attributes must be declared in source here instead.
[assembly: InternalsVisibleTo("Dorc.Core.Tests")]
[assembly: InternalsVisibleTo("Dorc.Monitor.Tests")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("5a87b1d6-7827-4e94-acbc-e554a2582283")]