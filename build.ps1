$version = '777.77.7'
$configuration = 'Release'

dotnet build .\tasks\Altemiq.DotNet.CodingStandard.Tasks --configuration $configuration -property:Version=$version

$sha = (git rev-parse HEAD)
$ref_name = (git symbolic-ref --short HEAD)
$repositoryUrl = (git remote get-url origin)
nuget pack Altemiq.DotNet.CodingStandard.nuspec -ForceEnglishOutput -Version $version -Properties "RepositoryCommit=$sha;RepositoryBranch=$ref_name;RepositoryUrl=$repositoryUrl;NoWarn=NU5100;Configuration=$configuration"