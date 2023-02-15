# GuidUpdater

Updates a folder in Unity with guids from another folder. Used for an issue with N-Hance assets.

## Purpose

To resolve issues with N-Hance assets, that at some point changed GUIDs for all their assets.
This means a project using old packages (for example Modular Fantasy Stylized Human Male/Female) can not easily be extended with packages with the new set of guids (for example a new outfit).

It also means updating the base packets will not work directly, as the package manager can not realize the folders and files are the same.
Additionally, if simple overwriting the old assets with the new set of files, any references in the project to the assets will be broken.

## Solution

A script can run through the old assets, and update them with new guids based on the file paths and names relative to the base folder.
This requires both versions to exist in the project. For example, the old package would be placed in "Asseets/Asset Packs/StylizedCharacter" and the new in "Assets/StylizedCharacter". To find the correct folders, known new and old guids of that folder is hard coded in the script.

Additionally, the script must resolve and update any references to the old assets. For example, there might be many game objects in the project that has the NHItem component, which must now know to use the new guid.

To avoid conflicts of GUIDs, the script must also set the GUID of the new asset to something else, so the old assets may receive their GUIDs. It could be a random new GUID, but this script will just swap them with the old assets.

## Usage

* Start with your current project, including the old assets. Make sure it is backed up.
* Put this script in a folder like Assets/Editor. Make sure it compiles neatly before continuing.
* Import the new base packages and any extensions that have the same issues. Expect to have many compile issues due to scripts being duplicated.
* Run the script from Main Menu -> Assets -> Voodoocado -> Update GUIDs.
* After some time, the update will be done. Unity will then have to import files again.
* Delete the new base packages, as they will have served their purpose.
* You can now import the new packages again from the package manager, which should sync well and show you if there is any changes to the actual asset files. 
