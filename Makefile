all:
	@./build.sh

paket.restore:
	@mono .paket/paket.exe restore

paket.install:
	@mono .paket/paket.exe install
