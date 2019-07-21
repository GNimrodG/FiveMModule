# FiveM Module to AMP

## Features
- Resource managerment
- Custom startup arguments
- Easy config for all deafult server vars

## Disadvantages
- Only work on windows
- Can't write in the console

##### Download
If you don't want to compile the project yourself then you can download it from the [releases](https://github.com/GNG2017/AMP-FiveM/releases).

## Install
1. Create a instance with any type of game.
2. Stop that instance and go into the datastore of that instance. On Linux systems you can find the AMP data store in the users home directory under `~/.ampdata`. On Windows systems you can select 'browse datastore' by right clicking an instance in the instance manager.
3. Go in the `Plugins` directory, then create a new folder with the name of `FiveMModule`, then paste the DLL file from the download in that directory.
4. Go back to the main folder of the instance and find `AMPConfig.conf` in that file change `AMP.AppModule` to `AMP.AppModule=FiveMModule`.
