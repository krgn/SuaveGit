all:
	@./build.sh

test:
	@./build.sh Test

paket.restore:
	@mono .paket/paket.exe restore

paket.pack:
	@mono .paket/paket.exe pack lock-dependencies output deploy/

paket.install:
	@mono .paket/paket.exe install
