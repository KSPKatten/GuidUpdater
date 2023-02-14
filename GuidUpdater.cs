
#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Voodoocado
{
    public class AssetGUIDRegeneratorMenu
    {
        public const string Version = "1.0";

        [MenuItem("Assets/Voodoocado/Upgrade GUIDs")]
        public static void UpgradeGUIDs()
        {
            bool option = EditorUtility.DisplayDialog($"Upgrade GUIDs for NHance asset/s",
                "This script will update an outdated folder structure by copying guids from a freshly installed copy of an asset. \n\nIt might take a long time.\n\nDo you want to proceed?", "Yes");

            if (option)
            {
                AssetDatabase.StartAssetEditing();
                try
                { 
                    AssetGUIDRegenerator.UpdateGUIDs();
                }
                catch(Exception e)
                {
                    Debug.LogError(e);
                }
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
    }
    internal class AssetGUIDRegenerator
    {
        // Basically, we want to limit the types here (e.g. "t:GameObject t:Scene t:Material").
        // But to support ScriptableObjects dynamically, we just include the base of all assets which is "t:Object"
        private const string SearchFilter = "t:Object";

        public static void UpdateGUIDs()
        {
            string guidToUpdate = "f6c90ecc3623ea84fbcd10153e8dcabb";
            string guidToTakeGUIDsFrom = "37c611f499cb40bc93b707bcc4bbe39c";

            string folderToUpdate = AssetDatabase.GUIDToAssetPath(guidToUpdate);
            string folderToTakeGUIDsFrom = AssetDatabase.GUIDToAssetPath(guidToTakeGUIDsFrom);

            Debug.Log("folderToUpdate="+folderToUpdate+ ", folderToTakeGUIDsFrom="+ folderToTakeGUIDsFrom);

            if (string.IsNullOrEmpty(folderToUpdate))
                throw new("Failed to find folder to update");
            if (string.IsNullOrEmpty(folderToTakeGUIDsFrom))
                throw new("Failed to find folder to take GUIDs from");

            // Get a list of assets that we should potentially 
            List<string> assetsToUpdate = new(AssetDatabase.FindAssets(SearchFilter, new string[] { folderToUpdate }));
            // Add the folder itself too
            assetsToUpdate.Add(guidToUpdate);

            // Where sould we pick the guids from?
            List<string> assetsToTakeGUIDsFrom = new(AssetDatabase.FindAssets(SearchFilter, new string[] { folderToTakeGUIDsFrom}));
            // Add the folder itself too
            assetsToTakeGUIDsFrom.Add(guidToTakeGUIDsFrom);

            // Precache the paths for these assets
            List<string> pathsToTakeGUIDsFrom = new();
            foreach(string asset in assetsToTakeGUIDsFrom)
                pathsToTakeGUIDsFrom.Add(AssetDatabase.GUIDToAssetPath(asset).Replace(folderToTakeGUIDsFrom, null));

            // Now crossrefernce this with the assets to update from
            // Throw away anything that isn't relevant
            int originalAssetCount = assetsToUpdate.Count;
            for (int i = 0; i < assetsToUpdate.Count; i++)
            {
                string asset = assetsToUpdate[i];
                string absolutePath = AssetDatabase.GUIDToAssetPath(asset);
                string relativePath = absolutePath.Replace(folderToUpdate, null);
                if (!pathsToTakeGUIDsFrom.Contains(relativePath))
                {
                    assetsToUpdate.Remove(asset);
                    i--;
                }
            }
            int filterdAssetCount = assetsToUpdate.Count;
            Debug.Log("Ready to update " + filterdAssetCount + "/" + originalAssetCount);

            var updatedAssets = new Dictionary<string, int>();
            var inverseReferenceMap = new Dictionary<string, HashSet<string>>();

            /*
            * PREPARATION PART 1 - Initialize map to store all paths that have a reference to our selectedGUIDs
            */
            foreach (var selectedGUID in assetsToUpdate)
            {
                inverseReferenceMap[selectedGUID] = new HashSet<string>();
            }

            /*
             * PREPARATION PART 2 - Scan all assets and store the inverse reference if contains a reference to any selectedGUI...
             */
            string[] assetGUIDs = AssetDatabase.FindAssets(SearchFilter, new string[] { "Assets" });
            var scanProgress = 0;
            var referencesCount = 0;
            foreach (var guid in assetGUIDs)
            {
                scanProgress++;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsDirectory(path)) continue;

                var dependencies = AssetDatabase.GetDependencies(path);
                foreach (var dependency in dependencies)
                {
                    EditorUtility.DisplayProgressBar($"Scanning guid references on:", path, (float)scanProgress / assetGUIDs.Length);

                    var dependencyGUID = AssetDatabase.AssetPathToGUID(dependency);
                    if (inverseReferenceMap.ContainsKey(dependencyGUID))
                    {
                        inverseReferenceMap[dependencyGUID].Add(path);

                        // Also include .meta path. This fixes broken references when an FBX uses external materials
                        var metaPath = AssetDatabase.GetTextMetaFilePathFromAssetPath(path);
                        inverseReferenceMap[dependencyGUID].Add(metaPath);

                        referencesCount++;
                    }
                }
            }

            var countProgress = 0;

            foreach (var asset in assetsToUpdate)
            {
                string absolutePath = AssetDatabase.GUIDToAssetPath(asset);
                string relativePath = absolutePath.Replace(folderToUpdate, null);
                int index = pathsToTakeGUIDsFrom.IndexOf(relativePath);

                // Take the new guid from the folder to take guids from
                var newGUID = assetsToTakeGUIDsFrom[index];

                /*
                    * PART 1 - Replace the GUID of the selected asset itself. If the .meta file does not exists or does not match the guid (which shouldn't happen), do not proceed to part 2
                    */
                var assetPath = AssetDatabase.GUIDToAssetPath(asset);

                // Update the old asset with the new guid
                UpdateMetaFile(asset, newGUID);

                // Also update the new asset with the old guid, avoiding conflicts 
                // in guids and automatic reverts by Unity.
                UpdateMetaFile(newGUID, asset);

                if (IsDirectory(assetPath))
                {
                    // Skip PART 2 for directories as they should not have any references in assets or scenes
                    updatedAssets.Add(AssetDatabase.GUIDToAssetPath(asset), 0);
                    continue;
                }

                /*
                    * PART 2 - Update the GUID for all assets that references the selected GUID
                    */
                var countReplaced = 0;
                var referencePaths = inverseReferenceMap[asset];
                foreach (var referencePath in referencePaths)
                {
                    countProgress++;

                    EditorUtility.DisplayProgressBar($"Regenerating GUID: {assetPath}", referencePath, (float)countProgress / referencesCount);

                    if (IsDirectory(referencePath)) continue;

                    var contents = File.ReadAllText(referencePath);

                    if (!contents.Contains(asset)) continue;

                    contents = contents.Replace(asset, newGUID);
                    File.WriteAllText(referencePath, contents);

                    countReplaced++;
                }
                updatedAssets.Add(AssetDatabase.GUIDToAssetPath(asset), countReplaced);
            }

            if (EditorUtility.DisplayDialog("Update GUIDs",
                $"Updated GUID for {updatedAssets.Count} assets. \nSee console logs for detailed report.", "Done"))
            {
                var message = $"<b>GUID Regenerator {AssetGUIDRegeneratorMenu.Version}</b>\n";

                if (updatedAssets.Count > 0) message += $"<b><color=green>{updatedAssets.Count} Updated Asset/s</color></b>\tSelect this log for more info\n";
                message = updatedAssets.Aggregate(message, (current, kvp) => current + $"{kvp.Value} references\t{kvp.Key}\n");

                Debug.Log($"{message}");
            }
        }

        private static void UpdateMetaFile(string guid, string newGuid)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var metaPath = AssetDatabase.GetTextMetaFilePathFromAssetPath(assetPath);

            if (!File.Exists(metaPath))
                throw new FileNotFoundException($"The meta file of selected asset cannot be found. Asset: {assetPath}");

            var metaAttributes = File.GetAttributes(metaPath);
            var bIsInitiallyHidden = false;

            // If the .meta file is hidden, unhide it temporarily
            if (metaAttributes.HasFlag(FileAttributes.Hidden))
            {
                bIsInitiallyHidden = true;
                HideFile(metaPath, metaAttributes);
            }

            var metaContents = File.ReadAllText(metaPath);
            // Check if guid in .meta file matches the guid of selected asset
            if (!metaContents.Contains(guid))
                throw new ArgumentException($"The GUID of [{assetPath}] does not match the GUID in its meta file.");

            // Update the old asset with the new guid
            metaContents = metaContents.Replace(guid, newGuid);
            File.WriteAllText(metaPath, metaContents);

            if (bIsInitiallyHidden)
                UnhideFile(metaPath, metaAttributes);
        }

        // Searches for Directories and extracts all asset guids inside it using AssetDatabase.FindAssets
        public static string[] ExtractGUIDs(string[] selectedGUIDs)
        {
            var finalGuids = new List<string>();
            foreach (var guid in selectedGUIDs)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (IsDirectory(assetPath))
                {
                    string[] searchDirectory = { assetPath };
                    finalGuids.Add(guid);
                    finalGuids.AddRange(AssetDatabase.FindAssets(SearchFilter, searchDirectory));
                }
                else
                {
                    finalGuids.Add(guid);
                }
            }

            return finalGuids.ToArray();
        }

        private static void HideFile(string path, FileAttributes attributes)
        {
            attributes &= ~FileAttributes.Hidden;
            File.SetAttributes(path, attributes);
        }

        private static void UnhideFile(string path, FileAttributes attributes)
        {
            attributes |= FileAttributes.Hidden;
            File.SetAttributes(path, attributes);
        }

        public static bool IsDirectory(string path) => File.GetAttributes(path).HasFlag(FileAttributes.Directory);
    }
}

#endif