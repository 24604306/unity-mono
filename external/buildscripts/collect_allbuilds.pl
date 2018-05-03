use lib ('./perl_lib');
use Cwd 'abs_path';
use File::Basename;
use File::Copy::Recursive qw(dircopy rmove);
use File::Path;
use Tools qw(InstallNameTool);

my $monoroot = File::Spec->rel2abs(dirname(__FILE__) . "/../..");
my $monoroot = abs_path($monoroot);

my $path = "incomingbuilds/";

rmtree("collectedbuilds");
mkpath("collectedbuilds");

my @folders = ();
opendir(DIR, $path) or die "cant find $path: $!";
while (defined(my $file = readdir(DIR))) {
	next if $file =~ /^\.\.?$/;
	if (-d "$path$file"){
		if (-f "$path$file/versions.txt") {
			system("cat $path$file/versions.txt >> collectedbuilds/versions-aggregated.txt");
			system("echo \"\" >> collectedbuilds/versions-aggregated.txt");
		}
		dircopy("$path$file","collectedbuilds/") or die ("failed copying $path$file");
		push @folders,"$path$file";
	}
}
closedir(DIR);

system("find collectedbuilds -type f -name mono -exec chmod +x {} \\;") eq 0 or die("Failed chmodding");
system("find collectedbuilds -type f -name mono-sgen -exec chmod +x {} \\;") eq 0 or die("Failed chmodding");
system("find collectedbuilds -type f -name pedump -exec chmod +x {} \\;") eq 0 or die("Failed chmodding");

chdir("collectedbuilds");

rmove('versions-aggregated.txt', 'versions.txt');

open(MYFILE,">built_by_teamcity.txt");
print MYFILE "These builds were created by teamcity from svn revision $ENV{BUILD_VCS_NUMBER}\n";
print MYFILE "TC projectname was: $ENV{TEAMCITY_PROJECT_NAME}\n";
print MYFILE "TC buildconfigname was: $ENV{TEAMCITY_BUILDCONF_NAME}\n";
close(MYFILE);

system("zip -r builds.zip *") eq 0 or die("failed zipping up builds");

if($^O eq "linux")
{
	system("$monoroot/../../mono-build-deps/build/7z/linux64/7za a builds.7z * -x!builds.zip") eq 0 or die("failed 7z up builds");
}
elsif($^O eq 'darwin')
{
	system("$monoroot/../../mono-build-deps/build/7z/osx/7za a builds.7z * -x!builds.zip") eq 0 or die("failed 7z up builds");
}
else
{
	die("Unsupported platform for build collection.")
}
