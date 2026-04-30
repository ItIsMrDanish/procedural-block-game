using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

public static class SaveSystem {

    public static void SaveWorld(WorldData world) {

        string savePath = World.Instance.appPath + "/saves/" + world.worldName + "/";

        if (!Directory.Exists(savePath))
            Directory.CreateDirectory(savePath);

        Debug.Log("Saving " + world.worldName);

        BinaryFormatter formatter = new BinaryFormatter();
        FileStream stream = new FileStream(savePath + "world.world", FileMode.Create);
        formatter.Serialize(stream, world);
        stream.Close();

        Thread thread = new Thread(() => SaveChunks(world));
        thread.Start();
    }

    public static void SaveChunks(WorldData world) {

        // FIX: modifiedChunks is now protected by a lock inside WorldData.
        // Use GetAndClearModifiedChunks() which atomically snapshots and clears
        // the list, instead of directly accessing modifiedChunks from a bg thread.
        List<ChunkData> chunks = world.GetAndClearModifiedChunks();

        int count = 0;
        foreach (ChunkData chunk in chunks) {

            SaveChunk(chunk, world.worldName);
            count++;
        }

        Debug.Log(count + " chunks saved.");
    }

    public static WorldData LoadWorld(string worldName, int seed = 0) {

        string loadPath = World.Instance.appPath + "/saves/" + worldName + "/";

        if (File.Exists(loadPath + "world.world")) {

            Debug.Log(worldName + " found. Loading from save.");

            BinaryFormatter formatter = new BinaryFormatter();
            FileStream stream = new FileStream(loadPath + "world.world", FileMode.Open);
            WorldData world = formatter.Deserialize(stream) as WorldData;
            stream.Close();
            return new WorldData(world);
        } else {

            Debug.Log(worldName + " not found. Creating new world.");
            WorldData world = new WorldData(worldName, seed);
            SaveWorld(world);
            return world;
        }
    }

    public static void SaveChunk(ChunkData chunk, string worldName) {

        // Include Y in filename so vertical chunks don't overwrite each other.
        string chunkName = $"{chunk.position.x}_{chunk.position.y}_{chunk.position.z}";
        string savePath = World.Instance.appPath + "/saves/" + worldName + "/chunks/";

        if (!Directory.Exists(savePath))
            Directory.CreateDirectory(savePath);

        BinaryFormatter formatter = new BinaryFormatter();
        FileStream stream = new FileStream(savePath + chunkName + ".chunk", FileMode.Create);
        formatter.Serialize(stream, chunk);
        stream.Close();
    }

    // Load using Vector3Int key (block-space chunk origin).
    public static ChunkData LoadChunk(string worldName, Vector3Int blockOrigin) {

        string chunkName = $"{blockOrigin.x}_{blockOrigin.y}_{blockOrigin.z}";
        string loadPath = World.Instance.appPath + "/saves/" + worldName + "/chunks/" + chunkName + ".chunk";

        if (File.Exists(loadPath)) {

            BinaryFormatter formatter = new BinaryFormatter();
            FileStream stream = new FileStream(loadPath, FileMode.Open);
            ChunkData chunkData = formatter.Deserialize(stream) as ChunkData;
            stream.Close();
            return chunkData;

        }

        return null;
    }
}