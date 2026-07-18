export namespace main {
	
	export class DownloadModel {
	    name: string;
	    url: string;
	    dir: string;
	
	    static createFrom(source: any = {}) {
	        return new DownloadModel(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.name = source["name"];
	        this.url = source["url"];
	        this.dir = source["dir"];
	    }
	}
	export class DownloaderStatus {
	    goDownloaderAvailable: boolean;
	    goDownloaderPath: string;
	
	    static createFrom(source: any = {}) {
	        return new DownloaderStatus(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.goDownloaderAvailable = source["goDownloaderAvailable"];
	        this.goDownloaderPath = source["goDownloaderPath"];
	    }
	}
	export class ProxyInfo {
	    uri: string;
	    username: string;
	    password: string;
	
	    static createFrom(source: any = {}) {
	        return new ProxyInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.uri = source["uri"];
	        this.username = source["username"];
	        this.password = source["password"];
	    }
	}
	export class ScannerStatus {
	    scannerAvailable: boolean;
	    scannerPath: string;
	    version: string;
	
	    static createFrom(source: any = {}) {
	        return new ScannerStatus(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.scannerAvailable = source["scannerAvailable"];
	        this.scannerPath = source["scannerPath"];
	        this.version = source["version"];
	    }
	}
	export class SideloaderStatus {
	    sideloaderAvailable: boolean;
	    sideloaderPath: string;
	    version: string;
	
	    static createFrom(source: any = {}) {
	        return new SideloaderStatus(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.sideloaderAvailable = source["sideloaderAvailable"];
	        this.sideloaderPath = source["sideloaderPath"];
	        this.version = source["version"];
	    }
	}

}

export namespace scanner {
	
	export class Options {
	    excludeDir?: string[];
	    targetExtension?: string[];
	
	    static createFrom(source: any = {}) {
	        return new Options(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.excludeDir = source["excludeDir"];
	        this.targetExtension = source["targetExtension"];
	    }
	}
	export class PngImageResult {
	    path: string;
	    imageData: string;
	
	    static createFrom(source: any = {}) {
	        return new PngImageResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.path = source["path"];
	        this.imageData = source["imageData"];
	    }
	}
	export class PngModResult {
	    path: string;
	    modIds: string[];
	
	    static createFrom(source: any = {}) {
	        return new PngModResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.path = source["path"];
	        this.modIds = source["modIds"];
	    }
	}
	export class PngNamesResult {
	    path: string;
	    names: string[];
	
	    static createFrom(source: any = {}) {
	        return new PngNamesResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.path = source["path"];
	        this.names = source["names"];
	    }
	}
	export class PngPageDataResult {
	    path: string;
	    names: string[];
	    imageData: string;
	
	    static createFrom(source: any = {}) {
	        return new PngPageDataResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.path = source["path"];
	        this.names = source["names"];
	        this.imageData = source["imageData"];
	    }
	}

}

