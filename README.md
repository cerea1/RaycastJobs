# RaycastJobs
Workaround for Unity RaycastCommand maxHit limitation

# Known issues
* RaycastAllCommand uses parameter minStep while scheduling internal jobs to overcome an issue with immediate hit near mesh collider corners
* In contrast to SpherecastAll SpherecastAllCommand could not return hit when ray origin inside a collider

# Installation
* Via Package Manager:
  * 2019.3+:
    * Add package from git URL... -> "https://github.com/cerea1/RaycastJobs.git"
  * earlier versions:
    * Add the line below to manifest.json inside Packages folder
    ```
      "com.raycastjobs": "git+https://github.com/cerea1/RaycastJobs.git"
    ```
* or clone from git to your project's Assets directory

# Usage
Refer to RaycastCommand docs https://docs.unity3d.com/ScriptReference/RaycastCommand.html
Note that each RaycastCommand.maxHits value should equals to 1 in order to use RaycastAllCommand.
Also manual disposing a RaycastAllCommand is required because it internaly allocates NativeArrays.
```
// preparing NativeArrays
...

var raycastAllCommand = new SpherecastAllCommand(commands, results, maxHits);
raycastAllCommand.Schedule(default(JobHandle)).Complete();

raycastAllCommand.Dispose();

...
// process results
// dispose NativeArrays

```
Proper job scheduling is supported so you can schedule a job in one frame and process results in the next frame.

SpherecastAllCommand usage is almost identical.
