'Site1
site1="http://localhost/FileSearch/LoadBranches"



Function WarmUp(url)
    On Error Resume Next
    Dim objHTTP
    Set objHTTP= CreateObject("Microsoft.XMLHTTP")
    objHTTP.Open "GET",url,False
    objHTTP.Send()
    If Err.Number=0 And objHTTP.Status=200 Then
        Hget=url & "has been warmed up successfully at :"& Date()&"  "& Time()
    Else
        Hget=url & "found error at :"& Date()&"  "& Time()
    End If
    Set objHTTP = Nothing
    'Section for writing into a text file
    Const FOR_APPENDING = 8
    strFileName = "service_status.txt"
    Set objFS = CreateObject("Scripting.FileSystemObject")
    Set objTS = objFS.OpenTextFile(strFileName,FOR_APPENDING)
    objTS.WriteLine Hget
	
End Function



WarmUp("http://localhost")