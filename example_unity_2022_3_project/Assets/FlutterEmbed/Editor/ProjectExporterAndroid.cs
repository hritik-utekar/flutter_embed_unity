using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

internal class ProjectExporterAndroid : ProjectExporter
{
    protected override void TransformExportedProject(string exportPath)
    {
        Debug.Log("Transforming Unity export for Flutter integration...");

        // The exported project has this structure:
        // 
        // <unityLibrary>
        // |
        // |- <gradle>
        // |- <launcher>
        // |- <unityLibrary>
        // |- build.gradle
        // |- gradle.properties
        // |- local.properties
        // |- settings.gradle
        //
        // The structure is:
        // A library part in the unityLibrary module that you can integrate into any other Gradle project. This contains the Unity runtime and Player data.
        // A thin launcher part in the launcher module that contains the application name and its icons. This is a simple Android application that launches Unity.
        //
        // This needs to be transformed into a single module (the unityLibrary part).
        // The launcher module is not needed, as the user's Flutter project's android part will be the 'launcher'.
        // However, we do need the string.xml file from the launcher, as this contains some strings which Unity
        // will expect to find (and will crash with android.content.res.Resources$NotFoundException if they don't exist)
        // So, first, copy strings.xml 
        // from `<exportPath>\launcher\src\main\res\values\` 
        // to   `<exportPath>\unityLibrary\src\main\res\values\`

        string stringsResourceFileFromPath = new string[] {exportPath, "launcher", "src", "main", "res", "values", "strings.xml"}
                .Aggregate((a, b) => Path.Combine(a, b));
        string stringResourcesFileToPath = new string[] {exportPath, "unityLibrary", "src", "main", "res", "values", "strings.xml"}
                .Aggregate((a, b) => Path.Combine(a, b));

        FileInfo stringsResourceFile = new FileInfo(stringsResourceFileFromPath);

        if(!stringsResourceFile.Exists) {
            ProjectExportHelpers.ShowErrorMessage($"Unexpected error: '{stringsResourceFile.FullName} not found");
            return;
        }

        stringsResourceFile.MoveTo(stringResourcesFileToPath);
        Debug.Log($"Moved {stringsResourceFileFromPath} to {stringResourcesFileToPath}");

        // Inspect gradle.properties and report the value of unityStreamingAssets, which should be added
        // to the user's android/gradle.properties
        FileInfo gradlePropertiesFile = new FileInfo(Path.Combine(exportPath, "gradle.properties"));
        if(gradlePropertiesFile.Exists) {
            string gradlePropertiesContent = File.ReadAllText(gradlePropertiesFile.FullName);
            Match unityStreamingAssetsMatch = (new Regex(@"(?<=unityStreamingAssets=).*", RegexOptions.Multiline)).Match(gradlePropertiesContent);
            if (unityStreamingAssetsMatch.Success)
            {
                Debug.Log($"The following should be added to your project's android/gradle.properties:\n" +
                    $"unityStreamingAssets={unityStreamingAssetsMatch.Value}");
            }
        }

        // The launcher folder can now be deleted
        DirectoryInfo launcherDirectory = new DirectoryInfo(Path.Combine(exportPath, "launcher"));
        Directory.Delete(launcherDirectory.FullName, true);
        Debug.Log($"Deleted {launcherDirectory.FullName}");

        // The gradle folder can be deleted
        DirectoryInfo gradleDirectory = new DirectoryInfo(Path.Combine(exportPath, "gradle"));
        Directory.Delete(gradleDirectory.FullName, true);
        Debug.Log($"Deleted {gradleDirectory.FullName}");

        // The files at the root of exportPath can be deleted
        DirectoryInfo exportDirectory = new DirectoryInfo(exportPath);
        foreach (FileInfo file in exportDirectory.GetFiles()) { 
            file.Delete(); 
            Debug.Log($"Deleted {file.FullName}");
        }

        // Now move the contents of 
        //    '<exportPath>/unityLibrary/unityLibrary' 
        // to '<exportPath>/unityLibrary'
        // so that the unityLibrary module is 'promoted' to being the main and only module of the export
        DirectoryInfo unityLibrarySubModuleDirectory = new DirectoryInfo(Path.Combine(exportPath, "unityLibrary"));
        if(!unityLibrarySubModuleDirectory.Exists) {
            ProjectExportHelpers.ShowErrorMessage($"Unexpected error: '{unityLibrarySubModuleDirectory.FullName} not found");
            return;
        }
        ProjectExportHelpers.MoveContentsOfDirectory(unityLibrarySubModuleDirectory, exportDirectory);
        unityLibrarySubModuleDirectory.Delete(true);
        Debug.Log($"Moved {unityLibrarySubModuleDirectory.FullName} to {exportDirectory.FullName}");

        // The export includes an activity in the AndroidManifest.xml which is not going to be
        // used (because we are using a Flutter PlatfromView instead). Remove it
        FileInfo androidManifestFile = new FileInfo(Path.Combine(exportPath, "src", "main", "AndroidManifest.xml"));
        if(!androidManifestFile.Exists) {
            ProjectExportHelpers.ShowErrorMessage($"Unexpected error: '{androidManifestFile.FullName} not found");
            return;
        }
        string androidManifestContents = File.ReadAllText(androidManifestFile.FullName);
        Regex regexActivityTag = new Regex(@"<activity.*>(\s|\S)+?</activity>", RegexOptions.Multiline);
        androidManifestContents = regexActivityTag.Replace(androidManifestContents, "<!-- activity block was removed by flutter_embed_unity exporter -->");
        File.WriteAllText(androidManifestFile.FullName, androidManifestContents);
        Debug.Log($"Removed <activity> from {androidManifestFile.FullName}");

        // Add the namespace 'com.unity3d.player' to unityLibrary\build.gradle
        // for compatibility with Gradle 8
        FileInfo buildGradleFile = new FileInfo(Path.Combine(exportPath, "build.gradle"));
        if(!buildGradleFile.Exists) {
            ProjectExportHelpers.ShowErrorMessage($"Unexpected error: '{buildGradleFile.FullName} not found");
            return;
        }
        string buildGradleContents = File.ReadAllText(buildGradleFile.FullName);
        // some unity versions might already include it
        if(!buildGradleContents.Contains("namespace")) {
            Regex regexAndroidBlock = new Regex(Regex.Escape("android {"));
            buildGradleContents = regexAndroidBlock.Replace(buildGradleContents, "android {\n\tnamespace 'com.unity3d.player'  // namespace was added by flutter_embed_unity exporter to support Gradle 8", 1);
            File.WriteAllText(buildGradleFile.FullName, buildGradleContents);
            Debug.Log($"Added namespace 'com.unity3d.player' to {buildGradleFile.FullName} for Gradle 8 compatibility");
        }
		
        // (optional) Add the namespace 'com.UnityTechnologies.XR.Manifest' to unityLibrary\xrmanifest.androidlib\build.gradle
        // for compatibility with Gradle 8
        FileInfo xrBuildGradleFile = new FileInfo(Path.Combine(exportPath, "xrmanifest.androidlib", "build.gradle"));
        if(xrBuildGradleFile.Exists) {
            string xrBuildGradleContents = File.ReadAllText(xrBuildGradleFile.FullName);
            // some unity versions might already include it
            if(!xrBuildGradleContents.Contains("namespace")) {
                Regex regexAndroidBlock = new Regex(Regex.Escape("android {"));
                xrBuildGradleContents = regexAndroidBlock.Replace(xrBuildGradleContents, "android {\n\tnamespace 'com.UnityTechnologies.XR.Manifest'  // namespace was added by flutter_embed_unity exporter to support Gradle 8", 1);
                File.WriteAllText(xrBuildGradleFile.FullName, xrBuildGradleContents);
                Debug.Log($"Added namespace 'com.UnityTechnologies.XR.Manifest' to {xrBuildGradleFile.FullName} for Gradle 8 compatibility");
            }
        }

        // Using project templates created with Flutter 3.29 or later now causes a build error due to an NDK version conflict.
        // For example:
        //
        // android.ndkVersion is [27.0.12077973] but android.ndkPath /Applications/Unity/Hub/Editor/2022.3.62f1/PlaybackEngines/AndroidPlayer/NDK 
        // refers to a different version [23.1.7779620]
        //
        // To resolve this we now need to remove the explicit reference to NDK 23.1 in unityLibrary\build.gradle, and allow the higher version
        // used by the Flutter project to take precendence. To do this, remove the line which begins with ndkPath, for example:
        // ndkPath "/Applications/Unity/Hub/Editor/2022.3.62f1/PlaybackEngines/AndroidPlayer/NDK"  (the exact path will vary)
        Regex regexNDKPath = new Regex(@"^.*ndkPath.*$", RegexOptions.Multiline);
        if (regexNDKPath.IsMatch(buildGradleContents))
        {
            buildGradleContents = regexNDKPath.Replace(buildGradleContents, "\t// ndkPath was removed by flutter_embed_unity exporter");
            File.WriteAllText(buildGradleFile.FullName, buildGradleContents);
            Debug.Log($"ndkPath property was removed from {buildGradleFile.FullName}");
        }

        DirectoryInfo burstDebugInformation = new DirectoryInfo(Path.Join(exportPath, "..", "unityLibrary_BurstDebugInformation_DoNotShip"));
        if(burstDebugInformation.Exists) {
            Directory.Delete(burstDebugInformation.FullName, true);
            Debug.Log($"Deleted {burstDebugInformation.FullName}");
        }

        Debug.Log("Transforming Unity export for Flutter integration complete");
    }

    
}
