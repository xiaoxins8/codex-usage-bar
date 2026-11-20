Unicode true
RequestExecutionLevel user
CRCCheck force
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "Sections.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!ifndef VERSION
  !define VERSION "0.6.2"
!endif
!ifndef PAYLOAD_ROOT
  !define PAYLOAD_ROOT "${__FILEDIR__}\.."
!endif
!ifndef SETUP_OUTPUT
  !define SETUP_OUTPUT "CodexUsageBar-Setup-v${VERSION}-x64-CustomPath.exe"
!endif

!ifdef QA_ROOT
  !define INSTALL_PATH "${QA_ROOT}\Installed"
  !define MENU_PATH "${QA_ROOT}\StartMenu"
  !define DESKTOP_PATH "${QA_ROOT}\Desktop"
  !define UNINSTALL_KEY "Software\CodexUsageBarInstallerQA\Uninstall"
  !define RUN_KEY "Software\CodexUsageBarInstallerQA\Run"
  !define APP_MUTEX "Local\CodexUsageBar.InstallerQA.Instance"
!else
  !define INSTALL_PATH "$LOCALAPPDATA\Programs\CodexUsageBar"
  !define MENU_PATH "$SMPROGRAMS\Codex 用量栏"
  !define DESKTOP_PATH "$DESKTOP"
  !define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexUsageBar"
  !define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
  !define APP_MUTEX "Local\CodexUsageBar.Instance"
!endif
!define DESKTOP_LINK "${DESKTOP_PATH}\Codex 用量栏.lnk"

Name "Codex 用量栏 ${VERSION}"
OutFile "${SETUP_OUTPUT}"
InstallDir "${INSTALL_PATH}"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
BrandingText "Codex 用量栏"
ShowInstDetails hide
ShowUninstDetails hide
VIProductVersion "${VERSION}.1"
VIAddVersionKey /LANG=2052 "ProductName" "Codex 用量栏"
VIAddVersionKey /LANG=2052 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "FileVersion" "${VERSION}.1"
VIAddVersionKey /LANG=2052 "FileDescription" "Codex 用量栏安装程序"
VIAddVersionKey /LANG=2052 "CompanyName" "Codex 用量栏"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Codex 用量栏"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装 Codex 用量栏 ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "为当前 Windows 用户安装 Codex 用量栏。点击「下一步」选择安装位置和快捷方式。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\CodexUsageBar.exe"
!define MUI_FINISHPAGE_RUN_TEXT "打开 Codex 用量栏"
!insertmacro MUI_PAGE_WELCOME
!define MUI_DIRECTORYPAGE_TEXT_TOP "可使用原目录更新，或选择另一个专用文件夹。更改位置后，安装程序会将已安装的用量栏迁到新位置。"
!define MUI_DIRECTORYPAGE_TEXT_DESTINATION "安装目录"
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateDestination
!insertmacro MUI_PAGE_DIRECTORY
!define MUI_COMPONENTSPAGE_NODESC
!define MUI_COMPONENTSPAGE_TEXT_TOP "选择是否添加桌面快捷方式。安装后可从开始菜单打开用量栏。"
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateDesktopChoice
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Var PreviousInstall
Var MoveStartup
Var PreviousDesktopShortcut
Var DesktopChoice

Section "Codex 用量栏" SecApplication
  SectionIn RO
SectionEnd

Section /o "在桌面创建快捷方式" SecDesktopShortcut
SectionEnd

Function .onInit
  ${IfNot} ${IsNativeAMD64}
  ${OrIfNot} ${AtLeastBuild} 17763
    MessageBox MB_OK|MB_ICONSTOP "此安装包适用于 Windows 10 1809 或更新的 Windows x64。" /SD IDOK
    SetErrorLevel 3
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
  ReadRegStr $PreviousInstall HKCU "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $PreviousInstall != ""
    GetFullPathName $PreviousInstall "$PreviousInstall"
    ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "UninstallString"
    ${If} $0 != '"$PreviousInstall\Uninstall.exe"'
      StrCpy $PreviousInstall ""
    ${Else}
      ReadRegDWORD $PreviousDesktopShortcut HKCU "${UNINSTALL_KEY}" "DesktopShortcut"
      ${If} $PreviousDesktopShortcut == 1
        !insertmacro SelectSection ${SecDesktopShortcut}
      ${EndIf}
    ${EndIf}
  ${EndIf}
  !ifdef QA_ROOT
    !ifdef QA_SELECT_DESKTOP
      !if "${QA_SELECT_DESKTOP}" == "1"
        !insertmacro SelectSection ${SecDesktopShortcut}
      !else
        !insertmacro UnselectSection ${SecDesktopShortcut}
      !endif
    !endif
  !endif
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext current
FunctionEnd

!macro CheckAppClosed prefix
Function ${prefix}CheckAppClosed
  check:
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "${APP_MUTEX}") p.r0'
  ${If} $0 == 0
    Return
  ${EndIf}
  System::Call 'kernel32::CloseHandle(p r0)'
  IfSilent stopped
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "请从系统托盘退出正在运行的 Codex 用量栏，然后点击「重试」。" IDRETRY check
  stopped:
  SetErrorLevel 2
  Abort
FunctionEnd
!macroend
!insertmacro CheckAppClosed ""
!insertmacro CheckAppClosed "un."

Function ValidateDestination
  ${If} $INSTDIR == ""
    Goto invalid
  ${EndIf}
  ; The Win32 call also normalizes a path whose folders have not been created yet.
  System::Call 'kernel32::GetFullPathNameW(w "$INSTDIR", i ${NSIS_MAX_STRLEN}, w .r0, p 0) i.r1'
  ${If} $1 <= 0
  ${OrIf} $1 >= ${NSIS_MAX_STRLEN}
    Goto invalid
  ${EndIf}
  StrCpy $INSTDIR "$0"
  StrLen $0 "$INSTDIR"
  ${If} $0 > 3
    StrCpy $0 "$INSTDIR" 1 -1
    ${If} $0 == "\"
      StrCpy $INSTDIR "$INSTDIR" -1
    ${EndIf}
  ${EndIf}
  ${If} $INSTDIR == "$PreviousInstall"
    Return
  ${EndIf}
  IfFileExists "$INSTDIR\CodexUsageBar.exe" occupied
  IfFileExists "$INSTDIR\Uninstall.exe" occupied
  IfFileExists "$INSTDIR\THIRD_PARTY_NOTICES.txt" occupied
  IfFileExists "$INSTDIR\WebView2-LICENSE.txt" occupied
  Return

  occupied:
    MessageBox MB_OK|MB_ICONEXCLAMATION "此文件夹已有同名程序或卸载文件。请选择一个新的用量栏专用文件夹。" /SD IDOK
    SetErrorLevel 4
    Abort

  invalid:
    MessageBox MB_OK|MB_ICONEXCLAMATION "请选择有效的安装目录。" /SD IDOK
    SetErrorLevel 4
    Abort
FunctionEnd

Function ValidateDesktopChoice
  StrCpy $DesktopChoice 0
  ${If} ${SectionIsSelected} ${SecDesktopShortcut}
    StrCpy $DesktopChoice 1
    ${If} $PreviousDesktopShortcut != 1
      IfFileExists "${DESKTOP_LINK}" occupied
    ${EndIf}
  ${EndIf}
  Return

  occupied:
    MessageBox MB_OK|MB_ICONEXCLAMATION "桌面已有同名快捷方式。请取消勾选，或重命名原快捷方式后继续。" /SD IDOK
    SetErrorLevel 4
    Abort
FunctionEnd

Section "-InstallApplication"
  Call CheckAppClosed
  Call ValidateDestination
  Call ValidateDesktopChoice
  StrCpy $MoveStartup 0
  ${If} $PreviousInstall != ""
  ${AndIf} $PreviousInstall != "$INSTDIR"
    ReadRegStr $0 HKCU "${RUN_KEY}" "CodexUsageBar"
    ${If} $0 == '"$PreviousInstall\CodexUsageBar.exe" --background'
      StrCpy $0 '"$INSTDIR\CodexUsageBar.exe" --background'
      StrLen $1 "$0"
      ${If} $1 > 260
        MessageBox MB_OK|MB_ICONSTOP "新路径太长，无法保留当前的开机自启。请选择较短的目录。" /SD IDOK
        SetErrorLevel 4
        Abort
      ${EndIf}
      StrCpy $MoveStartup 1
    ${EndIf}
  ${EndIf}
  ClearErrors
  SetOutPath "$INSTDIR"
  File "${PAYLOAD_ROOT}\CodexUsageBar.exe"
  File "${PAYLOAD_ROOT}\ui\THIRD_PARTY_NOTICES.txt"
  File /oname=WebView2-LICENSE.txt "${PAYLOAD_ROOT}\ui\vendor\webview2-LICENSE.txt"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfErrors failed

  CreateDirectory "${MENU_PATH}"
  CreateShortcut "${MENU_PATH}\Codex 用量栏.lnk" "$INSTDIR\CodexUsageBar.exe"
  IfErrors failed
  ${If} $DesktopChoice == 1
    CreateDirectory "${DESKTOP_PATH}"
    CreateShortcut "${DESKTOP_LINK}" "$INSTDIR\CodexUsageBar.exe"
    IfErrors failed
  ${ElseIf} $PreviousDesktopShortcut == 1
    Delete "${DESKTOP_LINK}"
    IfErrors failed
  ${EndIf}

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Codex 用量栏"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\CodexUsageBar.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "DesktopShortcut" "$DesktopChoice"
  IfErrors failed

  ${If} $MoveStartup == 1
    WriteRegStr HKCU "${RUN_KEY}" "CodexUsageBar" '"$INSTDIR\CodexUsageBar.exe" --background'
    IfErrors failed
  ${EndIf}
  ${If} $PreviousInstall != ""
  ${AndIf} $PreviousInstall != "$INSTDIR"
    Delete "$PreviousInstall\CodexUsageBar.exe"
    Delete "$PreviousInstall\THIRD_PARTY_NOTICES.txt"
    Delete "$PreviousInstall\WebView2-LICENSE.txt"
    Delete "$PreviousInstall\Uninstall.exe"
    RMDir "$PreviousInstall"
  ${EndIf}
  SetErrorLevel 0
  Goto done

  failed:
    MessageBox MB_OK|MB_ICONSTOP "安装未完成。请检查安装目录和快捷方式位置的访问权限，然后重新运行安装程序。" /SD IDOK
    SetErrorLevel 1
    Abort
  done:
SectionEnd

Section "Uninstall"
  Call un.CheckAppClosed
  ClearErrors
  Delete "$INSTDIR\CodexUsageBar.exe"
  IfErrors failed
  Delete "$INSTDIR\THIRD_PARTY_NOTICES.txt"
  Delete "$INSTDIR\WebView2-LICENSE.txt"
  ReadRegStr $0 HKCU "${RUN_KEY}" "CodexUsageBar"
  ${If} $0 == '"$INSTDIR\CodexUsageBar.exe" --background'
    DeleteRegValue HKCU "${RUN_KEY}" "CodexUsageBar"
  ${EndIf}
  ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $0 == "$INSTDIR"
    ReadRegDWORD $1 HKCU "${UNINSTALL_KEY}" "DesktopShortcut"
    ${If} $1 == 1
      Delete "${DESKTOP_LINK}"
    ${EndIf}
    Delete "${MENU_PATH}\Codex 用量栏.lnk"
    RMDir "${MENU_PATH}"
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
  ${EndIf}
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Goto done

  failed:
    MessageBox MB_OK|MB_ICONSTOP "无法移除用量栏程序。请关闭用量栏后重试。" /SD IDOK
    SetErrorLevel 1
    Abort
  done:
SectionEnd
