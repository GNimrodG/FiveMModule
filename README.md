# FiveM Module for AMP

## Features
- Resource management
- Custom startup arguments
- Easy config for all default server vars

## Disadvantages
- Only works on Windows
- Can't write in the console

## Download
If you don't want to compile the project yourself then you can download it from the [releases](https://github.com/GNG2017/AMP-FiveM/releases).

## Install
1. Create an instance with any type of game, using an AMP Professional licence (within the AMP web interface.) 

2. Start the instance, then stop it.

3. Go into the datastore of that instance. 
 - On Linux systems you can find the AMP datastore in the user's home directory under `~/.ampdata`. 
 - On Windows systems you can select 'browse datastore' by right clicking an instance in the AMP Instance Manager.

4. Goto the `Plugins` directory, create a new folder with the name of FiveMModule, then paste the DLL file from the download in that directory.

5. Go back to the main folder of the instance and find `AMPConfig.conf`, in that file change AMP.AppModule to `AMP.AppModule=FiveMModule`.

6. Reactivate the licence for the game instance using BOTH a Professional and Developer key by using the `ampinstmgr reactivate` command in Command Prompt/Terminal (run it twice, Professional then Developer key.)

7. Start the instance
