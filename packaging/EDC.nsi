; Installer for Elite Dangerous Companion (EDC: the music player and the companion), compiled by
; makensis from packaging/build-installer.sh.
;
; Compiled with: makensis -DVERSION=1.1.0 -DSOURCE_DIR=... -DICON=... -DOUT_FILE=... EDC.nsi
; SOURCE_DIR comes with the separators of the system makensis runs on: on Windows, a "File /r" wildcard
; under forward slashes finds nothing. Target paths use backslashes.

Unicode true

!include "MUI2.nsh"
!include "WinVer.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!ifndef VERSION
  !error "VERSION is required"
!endif
!ifndef SOURCE_DIR
  !error "SOURCE_DIR is required"
!endif
!ifndef OUT_FILE
  !error "OUT_FILE is required"
!endif
!ifndef ICON
  !error "ICON is required"
!endif

!define APP_NAME "Elite Dangerous Companion"
!define APP_EXE "EDC.exe"
!define PUBLISHER "Matthieu Fereyre"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\EDC"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUN_VALUE "EDC"

Name "${APP_NAME}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
; A personal desktop player: nothing outside the user profile, no elevation.
RequestExecutionLevel user
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}.0"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey "LegalCopyright" "${PUBLISHER}"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
; No page before the progress: double-clicking installs.
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_INSTFILES

; The pages follow the language of Windows: English, or French.
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "French"

;-------------------------------------------------------------------------------------------------
Function .onInit
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "${APP_NAME} needs Windows 10 or later."
    Abort
  ${EndIf}
  SetShellVarContext current
FunctionEnd

Section "Install"
  ; Windows will not overwrite a file the running player has mapped. Closing it is safe: the library
  ; and the settings are written on every change, never on the way out.
  DetailPrint "Closing ${APP_NAME}..."
  nsExec::Exec 'taskkill /IM ${APP_EXE} /F'
  Pop $0
  Sleep 500

  ; Emptying the folder first guarantees no file of the previous version survives. Guarded on the
  ; executable so a wrong INSTDIR removes nothing. The library, the log and the settings live in
  ; Saved Games, not here: nothing of the user's is lost.
  ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
    DetailPrint "Removing the previous version..."
    RMDir /r "$INSTDIR"
  ${EndIf}

  SetOutPath "$INSTDIR"
  File /r "${SOURCE_DIR}/*"

  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

  ; Started at log on from the first install, which is the point of a player that waits for the
  ; game. Only then: an update keeps the choice made since in the tray menu, but rewrites a value
  ; already there, which may point at the old folder.
  ReadRegStr $1 HKCU "${UNINSTALL_KEY}" "DisplayVersion"
  ReadRegStr $2 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${If} $1 == ""
  ${OrIf} $2 != ""
    WriteRegStr HKCU "${RUN_KEY}" "${RUN_VALUE}" '"$INSTDIR\${APP_EXE}"'
  ${EndIf}

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $0

  ; Closed above, so it comes back: otherwise the music would stay off until the next log on.
  DetailPrint "Starting ${APP_NAME}..."
  Exec '"$INSTDIR\${APP_EXE}"'
  SetAutoClose true
SectionEnd

;-------------------------------------------------------------------------------------------------
Function un.onInit
  SetShellVarContext current
FunctionEnd

Section "Uninstall"
  nsExec::Exec 'taskkill /IM ${APP_EXE} /F'
  Pop $0
  Sleep 500

  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  ; Left behind, it would point at an executable that no longer exists.
  DeleteRegValue HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ; The library and the settings stay in Saved Games: a reinstall finds them.
SectionEnd
