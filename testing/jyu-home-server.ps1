$methodListSinle = ("301","303") #,"305","333","335","703","705","733")
$message1 = ""

$role = "s"
$ipLocal = "84.251.221.209"
$ipRemote = "130.234.231.44"
$portLocal = "11011"
$portRemote = "11000"
$serverTimeout = "0" #in miliSeconds
$pathToExe = "D:\Dropbox\Visual Studio Projects\Projects\Steganography-for-IP-networks\SteganoNet.UI.Console\bin\Release"

&".\SteganoNetTester.ps1" -role $role -methods $methodListSinle -messages $message1 -ipLocal $ipLocal -portLocal $portLocal -ipremote $ipRemote -portremote $portRemote -runsame n -pathToExe $pathToExe -serverTimeout $serverTimeout