all: wax

_data/smr.json: _build-data/_data/smr.json
	cp -f _build-data/_data/smr.json _data/smr.json

_data/raw_images/smr: _build-data/_data/raw_images/smr
	rm -rf _data/raw_images/smr
	cp -rf _build-data/_data/raw_images/smr _data/raw_images/smr

wax: _data/smr.json _data/raw_images/smr
	bundle exec rake wax:derivatives:simple smr
	bundle exec rake wax:pages smr
	bundle exec rake wax:search main
	bundle exec jekyll build

clean:
	bundle exec rake wax:clobber smr
	rm -rf _data/smr.json _data/raw_images/smr