$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$tools=Join-Path $root '.tools/android-sdk/build-tools/android-15'
$androidJar=Join-Path $root '.tools/android-sdk/platforms/android-35/android.jar'
$stage=Join-Path $root '.local/remote-probe/android'
New-Item -ItemType Directory -Force "$stage/classes","$stage/dex" | Out-Null
function Native([string]$Exe,[string[]]$Arguments){& $Exe @Arguments;if($LASTEXITCODE -ne 0){throw "Failed: $Exe"}}
Native "$tools/aapt.exe" @('package','-f','-M',"$root/native/remote/probe/android/AndroidManifest.xml",'-I',$androidJar,'-F',"$stage/unsigned.apk")
Native 'javac' @('-encoding','UTF-8','--release','8','-classpath',$androidJar,'-d',"$stage/classes","$root/native/remote/probe/android/ProbeActivity.java","$root/android/app/src/main/java/com/codelink/app/RemoteNative.java")
Native 'jar' @('--create','--file',"$stage/classes.jar",'-C',"$stage/classes",'.')
Native "$tools/d8.bat" @('--lib',$androidJar,'--min-api','28','--output',"$stage/dex","$stage/classes.jar")
Push-Location "$stage/dex"
try{Native "$tools/aapt.exe" @('add','-f',"$stage/unsigned.apk",'classes.dex')}finally{Pop-Location}
Push-Location "$root/.local/remote-native/android"
try{Native "$tools/aapt.exe" @('add','-f',"$stage/unsigned.apk",'lib/arm64-v8a/libcodelink_remote.so')}finally{Pop-Location}
Native "$tools/zipalign.exe" @('-f','-P','16','4',"$stage/unsigned.apk","$stage/aligned.apk")
Native "$tools/apksigner.bat" @('sign','--ks',"$root/.local/CodeLink.debug.keystore",'--ks-key-alias','codelink','--ks-pass','pass:codelinkdebug','--key-pass','pass:codelinkdebug','--out',"$stage/probe.apk","$stage/aligned.apk")
Write-Output "Probe APK: $stage/probe.apk"
