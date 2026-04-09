; Cockpit Windows Installer — NSIS script
; Build: makensis /DAPP_VERSION=1.2.3 /DSOURCE_PATH=C:\path\to\publish /DOUTPUT_PATH=Cockpit-windows-x64-Setup.exe windows.nsi

!ifndef APP_VERSION
  !define APP_VERSION "0.0.0"
!endif
!ifndef SOURCE_PATH
  !error "SOURCE_PATH must be defined (/DSOURCE_PATH=...)"
!endif
!ifndef OUTPUT_PATH
  !define OUTPUT_PATH "Cockpit-windows-x64-Setup.exe"
!endif

!define APP_NAME      "Cockpit"
!define APP_EXE       "Cockpit.exe"
!define APP_ID        "com.ieuanwalker.cockpit"
!define APP_PUBLISHER "Ieuan Walker"
!define APP_URL       "https://github.com/ieuanwalker/Cockpit"

!include "MUI2.nsh"
SetCompressor /SOLID lzma

Unicode True
Name          "${APP_NAME} ${APP_VERSION}"
OutFile       "${OUTPUT_PATH}"
InstallDir    "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "Software\${APP_NAME}" "Install_Dir"
RequestExecutionLevel admin
ManifestDPIAware true
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright © ${APP_PUBLISHER}"
BrandingText  "${APP_NAME} ${APP_VERSION}"

; MUI settings
!define MUI_ABORTWARNING
!define MUI_ICON "logo.ico"
!define MUI_UNICON "logo.ico"
!define MUI_FINISHPAGE_RUN         "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT    "Launch ${APP_NAME}"

; Installer pages
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ── Install ──────────────────────────────────────────────────────────────────
Section "Cockpit" SecMain
  SetOutPath "$INSTDIR"
  File /r "${SOURCE_PATH}\*.*"

  ; Registry: install location + Apps & Features entry
  WriteRegStr   HKLM "Software\${APP_NAME}" "Install_Dir" "$INSTDIR"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "DisplayName"     "${APP_NAME}"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "DisplayVersion"  "${APP_VERSION}"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "Publisher"       "${APP_PUBLISHER}"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "URLInfoAbout"    "${APP_URL}"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "DisplayIcon"     "$INSTDIR\${APP_EXE}"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "NoRepair"  1

  WriteUninstaller "$INSTDIR\uninstall.exe"

  ; Start menu
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" \
    "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "$INSTDIR"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" \
    "$INSTDIR\uninstall.exe"

  ; Desktop shortcut
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" \
    "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "$INSTDIR"
SectionEnd

; ── Uninstall ─────────────────────────────────────────────────────────────────
Section "Uninstall"
  RMDir /r "$INSTDIR"
  Delete "$SMPROGRAMS\${APP_NAME}\*.*"
  RMDir  "$SMPROGRAMS\${APP_NAME}"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
  DeleteRegKey HKLM "Software\${APP_NAME}"
SectionEnd
