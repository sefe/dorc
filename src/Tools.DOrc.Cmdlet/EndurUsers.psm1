using module ".\Models.psm1"
using module ".\ApiCaller.psm1"

class EndurUsers{
    #[ApiCaller]$api
    hidden [string]$basePath

    EndurUsers([string]$url) {
        #$this.api=[ApiCaller]::new()
        $this.basePath=$url
    }

    [bool]UserExists([string] $samAccountName){
        $queryString="userName="+$samAccountName+"&userType=Endur"
        $url=[UserEndpoints]::UsersEndpoint($this.basePath)
        [ApiResult]$result=[ApiCaller]::InvokeGet($url,$queryString)
        if ($result.ReturnCode -ne 200){return $false}
        [User]$user=$result.Value
        if ($user.Id -gt 0 ){return $true}
        return $false
    }
    [bool]HasRights(){
        $devAdm=$false
        $appAdm=$false
        $members = Get-ADGroupMember -Identity "Development Admins" -Recursive | Select -ExpandProperty samAccountName
        If ($members -contains $env:USERNAME) {
            $devAdm=$true
        }
        $members = Get-ADGroupMember -Identity "Applications Admins" -Recursive | Select -ExpandProperty samAccountName
        If ($members -contains $env:USERNAME) {
            $appAdm=$true
        }      
        [bool]$predicate = $devAdm -or $appAdm;
        return $predicate
    }
    [bool]AddEndurUser([string] $samAccountName){
        [UserAdd]$user=[UserAdd]::new()
        $u=Get-ADUser -Filter {samAccountName -eq $samAccountName} -Properties *
        if (-Not $u) {write-host -ForegroundColor Red "Specified account not found in AD"; return $false}
        $user.DisplayName = $u.Name
        $user.LoginType = "Endur"
        $user.LanIdType = "USER"
        $user.LanId = $u.SamAccountName
        $user.Team = $u.Description
        $url=[UserEndpoints]::UsersEndpoint($this.basePath)
        $body=ConvertTo-Json $user
        [ApiResult]$response=[ApiCaller]::InvokePost($url,$body)
        if ($response.ReturnCode -eq 200 -and $response.Value.Id -gt 0) {return $true}
        return $false
    }
    

}

class UserEndpoints{   

    static [string]UsersEndpoint([string]$baseUrl){
        return $baseUrl + "/RefDataUsers"
    }
    static [string]SearcherEndpoint([string]$baseUrl){
        return $baseUrl + "/DirectorySearch/users/"
    }
    static [string]IsUserInGroupEndpoint([string]$baseUrl){
        return $baseUrl + "/DirectorySearch/isuseringroup"
    }

}

