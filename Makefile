all:
	@./build.sh

test:
	@./build.sh Test

paket.exe:
	@mono .paket/paket.bootstrapper.exe

paket.restore: paket.exe
	@mono .paket/paket.exe restore

paket.pack:
	@mono .paket/paket.exe pack lock-dependencies output deploy/

paket.install:
	@mono .paket/paket.exe install
