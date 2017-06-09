# Suave.Git

Serve git repositories (bare & non-bare) via HTTP from
[Suave](http://suave.io). These routes implement the [Smart HTTP
Protocol](https://github.com/git/git/blob/master/Documentation/technical/http-protocol.txt). 

The following operations are known to work:

- `git pull`
- `git push`
- `git clone`
- `git ls-remote`

# Example

A short example how this API is used:

```{.fsharp}

let dir = "/home/k/projects"
let name = "myproject"  // the directory containing .git (non-bare) or a bare repository

let createServer () =
  let cts = new CancellationTokenSource()

  let config =
    { defaultConfig with
        cancellationToken = cts.Token
        bindings = [ HttpBinding.create HTTP IPAddress.Loopback 7000us ] }

  // This will create the following routes:
  //
  // - GET  "/myproject/info/refs"
  // - POST "/myproject/git-upload-pack" 
  // - POST "/myproject/git-receive-pack" 
  //
  // If the first parmeter is `None`, the path will be in the root.
  
  Path.Combine(dir,name)
  |> gitServer (Some name) 
  |> startWebServerAsync config 
  |> (fun (_, server) -> Async.Start(server, cts.Token))

  // On very slow machines (AppVeyor) it sometimes takes
  // around 100ms, so we wait a little to make sure the server is up
  Thread.Sleep(150)

  { new IDisposable with
      member self.Dispose() =
        try
          cts.Cancel()
          cts.Dispose()
        with | _ -> () }

use server = createServer()

```

Once running, you can use the regular commands to add remotes and clone. 

# Configuration

If you intend to serve _non-bare_ repositories, make sure you set this
option on the repository to ensure `git push` will also update your
currently checked out branch.

```
git config --local receive.denyCurrentBranch updateInstead
```
