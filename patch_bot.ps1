$file = "c:\Users\Dell\Documents\vu tru dai chien\2026_02_11_OpenTest_015\AutoQuestPlugin\BotController.cs"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

# === PATCH 1: Add _guideErrorLogged field ===
$fieldSearch = "private MonoBehaviour _guideManager;"
$fieldReplace = "private MonoBehaviour _guideManager;`r`n        private bool _guideErrorLogged = false;"
if ($content.Contains($fieldSearch) -and -not $content.Contains("_guideErrorLogged")) {
    $content = $content.Replace($fieldSearch, $fieldReplace)
    Write-Host "PATCH 1 OK: Added _guideErrorLogged field"
} else {
    Write-Host "PATCH 1 SKIP: field already exists or search not found"
}

# === PATCH 2: Fix CallMethod to prefer 0-param overloads ===
# Replace the whole method body
$oldCallMethod = @"
        private void CallMethod(MonoBehaviour target, string methodName)
        {
            var methods = target.GetIl2CppType().GetMethods();
            foreach (var m in methods)
            {
                if (m.Name == methodName)
                {
                    m.Invoke(target, null);
                    return;
                }
            }
            throw new Exception(`$"Method '{methodName}' not found on {target.GetIl2CppType().Name}");
        }
"@

$newCallMethod = @"
        private void CallMethod(MonoBehaviour target, string methodName)
        {
            var methods = target.GetIl2CppType().GetMethods();
            Il2CppSystem.Reflection.MethodInfo bestMatch = null;
            foreach (var m in methods)
            {
                if (m.Name == methodName)
                {
                    var parms = m.GetParameters();
                    if (parms == null || parms.Length == 0)
                    {
                        m.Invoke(target, null);
                        return;
                    }
                    if (bestMatch == null) bestMatch = m;
                }
            }
            if (bestMatch != null)
            {
                bestMatch.Invoke(target, null);
                return;
            }
            throw new Exception(`$"Method '{methodName}' not found on {target.GetIl2CppType().Name}");
        }

        private void CallMethodNoParam(MonoBehaviour target, string methodName)
        {
            var methods = target.GetIl2CppType().GetMethods();
            foreach (var m in methods)
            {
                if (m.Name == methodName)
                {
                    var parms = m.GetParameters();
                    if (parms == null || parms.Length == 0)
                    {
                        m.Invoke(target, null);
                        return;
                    }
                }
            }
            throw new Exception(`$"No parameterless '{methodName}' on {target.GetIl2CppType().Name}");
        }
"@

if ($content.Contains("private void CallMethod(MonoBehaviour target, string methodName)") -and -not $content.Contains("CallMethodNoParam")) {
    $content = $content.Replace($oldCallMethod, $newCallMethod)
    if ($content.Contains("CallMethodNoParam")) {
        Write-Host "PATCH 2 OK: CallMethod improved + CallMethodNoParam added"
    } else {
        Write-Host "PATCH 2 PARTIAL: Replace did not fully work, trying line-by-line"
    }
} else {
    Write-Host "PATCH 2 SKIP: Already patched or method not found"
}

# === PATCH 3: Fix TryAutoRevive to search PopupCanvas ===
$oldReviveSearch = @"
            // Tim nut revive tren UI
            string[] reviveNames = {
                "ReviveButton", "BtnRevive", "ReviveBtn", "Revive",
                "FreeReviveButton", "FreeRevive", "ReviveHere"
            };
"@

$oldReviveSearch2 = @"
            // T`u00ecm n`u00fat revive tr`u00ean UI
            string[] reviveNames = {
                "ReviveButton", "BtnRevive", "ReviveBtn", "Revive",
                "FreeReviveButton", "FreeRevive", "ReviveHere"
            };
"@

# Try unicode version
$search3 = "// T" + [char]0xEC + "m n" + [char]0xFA + "t revive tr" + [char]0xEA + "n UI"
if ($content.Contains($search3)) {
    Write-Host "Found Vietnamese text for revive search (legacy encoding)"
}

# Also try UTF-8 Vietnamese
$searchUtf = [char]0x00EC  # this won't work in PS directly
# Let's search more generically
$reviveMarker = 'string[] reviveNames = {'
if ($content.Contains($reviveMarker)) {
    Write-Host "Found reviveNames array"
    # Replace the reviveNames array to add more search terms
    $oldArr = '"ReviveButton", "BtnRevive", "ReviveBtn", "Revive",'
    $newArr = '"ReviveButton", "BtnRevive", "ReviveBtn", "Revive", "ConfirmButton", "OKButton",'
    if ($content.Contains($oldArr)) {
        $content = $content.Replace($oldArr, $newArr)
        Write-Host "PATCH 3a OK: Added ConfirmButton, OKButton to revive search"
    }
    
    $oldArr2 = '"FreeReviveButton", "FreeRevive", "ReviveHere"'
    $newArr2 = '"FreeReviveButton", "FreeRevive", "ReviveHere", "AcceptButton"'
    if ($content.Contains($oldArr2)) {
        $content = $content.Replace($oldArr2, $newArr2)
        Write-Host "PATCH 3b OK: Added AcceptButton to revive search"
    }
}

# === PATCH 4: Add PopupCanvas search before reviveNames check ===
$reviveLogLine = '[Bot] ☠️ Nh'
if (-not $content.Contains("PopupCanvas search for revive")) {
    # Find the revive warning log line and add PopupCanvas search after it
    $insertAfter = '_reviveCooldown = 15f;'
    $popupSearch = @'

            // PopupCanvas search for revive buttons
            var popupCanvas = GameObject.Find("PopupCanvas");
            if (popupCanvas != null)
            {
                var popupBtns = popupCanvas.GetComponentsInChildren<Button>(true);
                foreach (var pbtn in popupBtns)
                {
                    if (pbtn == null || !pbtn.gameObject.activeSelf) continue;
                    string pbtnName = pbtn.gameObject.name ?? "";
                    if (pbtnName.Contains("Revive") || pbtnName.Contains("revive") ||
                        pbtnName.Contains("Confirm") || pbtnName.Contains("Accept"))
                    {
                        pbtn.onClick.Invoke();
                        Plugin.Log.LogInfo($"[Bot] ✨ Revive: Clicked '{pbtnName}' on PopupCanvas");
                        _reviveCooldown = 10f;
                        return;
                    }
                }
            }
'@
    # Actually let's insert PopupCanvas search BEFORE the reviveNames search
    if ($content.Contains($reviveMarker)) {
        $content = $content.Replace($reviveMarker, $popupSearch + "`r`n`r`n            " + $reviveMarker)
        Write-Host "PATCH 4 OK: Added PopupCanvas search for revive"
    }
} else {
    Write-Host "PATCH 4 SKIP: PopupCanvas search already exists"
}

# === PATCH 5: Fix AutoAttackBlackBoard search to use hierarchy ===
$oldBBSearch = '_autoAttackBlackBoard = FindSingletonByType("AutoAttackBlackBoardComponent");'
$newBBSearch = @'
_autoAttackBlackBoard = FindSingletonByType("AutoAttackBlackBoardComponent");
            // Fallback: search via hierarchy since AutoAttackBlackBoard is active=False
            if (_autoAttackBlackBoard == null)
            {
                var serviceGo = GameObject.Find("Service");
                if (serviceGo != null)
                {
                    var found = FindInactiveChild(serviceGo.transform, "AutoAttackBlackBoard");
                    if (found != null)
                    {
                        var comps = found.GetComponentsInChildren<MonoBehaviour>(true);
                        foreach (var c in comps)
                        {
                            if (c.GetIl2CppType().Name == "AutoAttackBlackBoardComponent")
                            {
                                _autoAttackBlackBoard = c;
                                Plugin.Log.LogInfo("[Bot] 🔍 Found AutoAttackBlackBoard via Service hierarchy");
                                break;
                            }
                        }
                    }
                }
            }
'@
if ($content.Contains($oldBBSearch) -and -not $content.Contains("Found AutoAttackBlackBoard via Service")) {
    # Only replace the first occurrence (in TryAutoRevive)
    $idx = $content.IndexOf($oldBBSearch)
    $content = $content.Remove($idx, $oldBBSearch.Length).Insert($idx, $newBBSearch)
    Write-Host "PATCH 5 OK: Added hierarchy search for AutoAttackBlackBoard"
} else {
    Write-Host "PATCH 5 SKIP: Already patched or search not found"
}

# Save
[System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::UTF8)
Write-Host "`nAll patches applied. File saved."
