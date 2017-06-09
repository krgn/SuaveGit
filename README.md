# Suave.Git

Serve git repositories (bare & non-bare) via HTTP from (Suave)[http://suave.io].

# Example

```{.fsharp}



```

# Configuration

If you intend to serve _non-bare_ repositories, make sure you set this
option on the repository to ensure `git push` will also update your
currently checked out branch.

```
git config --local receive.denyCurrentBranch updateInstead
```
