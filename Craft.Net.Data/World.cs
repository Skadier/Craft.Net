using System;
using System.Collections.Generic;
using System.Linq;
using Craft.Net.Data.Blocks;
using Craft.Net.Data.Generation;
using Craft.Net.Data.Entities;
using System.IO;
using System.Threading;
using Craft.Net.Data.Events;

namespace Craft.Net.Data
{
    /// <summary>
    /// Represents a horizontally infinite world of blocks with a fixed height of 256 blocks.
    /// </summary>
    public partial class World
    {
        public const int Height = 256;

        /// <summary>
        /// The name of this world.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The currently loaded regions for this world.
        /// </summary>
        public Dictionary<Vector3, Region> Regions { get; set; }
        /// <summary>
        /// The entities currently present in this world.
        /// </summary>
        public List<Entity> Entities { get; set; }
        /// <summary>
        /// The world generator used to create this world.
        /// </summary>
        public IWorldGenerator WorldGenerator { get; set; }
        /// <summary>
        /// Gets the directory this world uses to save and load regions.
        /// </summary>
        public string Directory { get; private set; }
        /// <summary>
        /// Set to true if block updates should be performed.
        /// </summary>
        public bool EnableBlockUpdates { get; set; }

        public Level Level { get; set; }

        public Timer EntityUpdateTimer { get; set; }

        protected List<TimedBlockUpdate> PendingBlockUpdates { get; set; }
        protected object PendingBlockUpdatesLock = new object();

        /// <summary>
        /// Creates a new world for client-side use.
        /// </summary>
        public World(Level level)
        {
            Level = level;
            Name = "world";
            Entities = new List<Entity>();
            Regions = new Dictionary<Vector3, Region>();
            EnableBlockUpdates = true;
            EntityUpdateTimer = new Timer(DoEntityUpdates, null, Level.TickLength, Level.TickLength);
            PendingBlockUpdates = new List<TimedBlockUpdate>();
        }

        /// <summary>
        /// Creates a new world for server-side use with the specified world generator.
        /// </summary>
        public World(Level level, IWorldGenerator worldGenerator) : this(level)
        {
            WorldGenerator = worldGenerator;
        }

        /// <summary>
        /// Creates a new world for server-side use with the specified world generator
        /// and the specified working directory.
        /// </summary>
        public World(Level level, IWorldGenerator worldGenerator, string directory) : this(level, worldGenerator)
        {
            Directory = directory;
            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);
        }

        private string clientLevelType;
        /// <summary>
        /// Gets the level type this world's generator produces.
        /// </summary>
        public string LevelType
        {
            get
            {
                if (WorldGenerator != null)
                    return WorldGenerator.LevelType;
                else
                    return clientLevelType;
            }
            set
            {
                if (WorldGenerator == null)
                    clientLevelType = value;
                else
                    throw new InvalidOperationException("For server-mode worlds, LevelType is read only.");
            }
        }

        /// <summary>
        /// Fires when a block in the world is changed.
        /// </summary>
        public event EventHandler<BlockChangedEventArgs> BlockChanged;

        public event EventHandler<EntityEventArgs> SpawnEntity;

        public event EventHandler<EntityEventArgs> DestroyEntity; // TODO: Move some code out of EntityManager

        /// <summary>
        /// Returns the chunk at the specific position
        /// </summary>
        /// <param name="position">Position in chunk coordinates</param>
        public Chunk GetChunk(Vector3 position)
        {
            //In chunks
            var x = (int)position.X;
            var z = (int)position.Z;

            //In regions
            int regionX = x / Region.Width - ((x < 0) ? 1 : 0);
            int regionZ = z / Region.Depth - ((z < 0) ? 1 : 0);

            var region = CreateOrLoadRegion(new Vector3(regionX, 0, regionZ));
            return region.GetChunk(new Vector3(x - regionX * 32, 0, z - regionZ * 32));
        }

        /// <summary>
        /// Returns the chunk at the specific position, but does not generate
        /// a new chunk if it doesn't already exist.
        /// </summary>
        /// <param name="position">Position in chunk coordinates</param>
        public Chunk GetChunkWithoutGeneration(Vector3 position)
        {
            //In chunks
            var x = (int)position.X;
            var z = (int)position.Z;

            //In regions
            int regionX = x / Region.Width - ((x < 0) ? 1 : 0);
            int regionZ = z / Region.Depth - ((z < 0) ? 1 : 0);

            var regionPosition = new Vector3(regionX, 0, regionZ);
            if (!Regions.ContainsKey(regionPosition)) return null;
            return Regions[regionPosition].GetChunkWithoutGeneration(new Vector3(x - regionX * 32, 0, z - regionZ * 32));
        }

        /// <summary>
        /// Sets the chunk at the given position to the chunk provided.
        /// </summary>
        public void SetChunk(Vector3 position, Chunk chunk)
        {
            //In chunks
            var x = (int)position.X;
            var z = (int)position.Z;

            //In regions
            int regionX = x/Region.Width - ((x < 0) ? 1 : 0);
            int regionZ = z/Region.Depth - ((z < 0) ? 1 : 0);

            var region = CreateOrLoadRegion(new Vector3(regionX, 0, regionZ));
            chunk.IsModified = true;
            chunk.ParentRegion = region;
            region.SetChunk(new Vector3(x - regionX * 32, 0, z - regionZ * 32), chunk);
        }

        public void UnloadChunk(int x, int z, bool save)
        {
            //In regions
            int regionX = x / Region.Width - ((x < 0) ? 1 : 0);
            int regionZ = z / Region.Depth - ((z < 0) ? 1 : 0);

            var position = new Vector3(regionX, 0, regionZ);
            if (!Regions.ContainsKey(position))
                return;
            var region = Regions[position];
            if (save)
                region.Save();
            region.UnloadChunk(new Vector3(x - regionX * 32, 0, z - regionZ * 32));
        }

        public void UnloadChunk(int x, int z)
        {
            UnloadChunk(x, z, false);
        }

        /// <summary>
        /// Gets the block at the specified position.
        /// </summary>
        public Block GetBlock(Vector3 position)
        {
            Chunk chunk;
            Vector3 blockPosition = FindBlockPosition(position, out chunk);

            return chunk.GetBlock(blockPosition);
        }

        /// <summary>
        /// Returns air for blocks outside the world without throwing an exception.
        /// </summary>
        public Block SafeGetBlock(Vector3 position)
        {
            if (position.Y < 0 || position.Y >= World.Height)
                return new AirBlock();

            Chunk chunk;
            Vector3 blockPosition = FindBlockPosition(position, out chunk);

            return chunk.GetBlock(blockPosition);
        }

        /// <summary>
        /// Sets the block at the specified position.
        /// </summary>
        public void SetBlock(Vector3 position, Block value)
        {
            Chunk chunk;
            Vector3 blockPosition = FindBlockPosition(position, out chunk);

            chunk.SetBlock(blockPosition, value);

            if (BlockChanged != null)
                BlockChanged(this, new BlockChangedEventArgs(this, position, value));

            DoBlockUpdates(position);
        }

        private void DoBlockUpdates(Vector3 blockPosition)
        {
            if (!EnableBlockUpdates)
                return;

            GetBlock(blockPosition).BlockUpdate(this, blockPosition, blockPosition);

            if ((blockPosition + Vector3.Up).Y < Chunk.Height)
                GetBlock(blockPosition + Vector3.Up).BlockUpdate(this, blockPosition + Vector3.Up, blockPosition);
            if ((blockPosition + Vector3.Down).Y >= 0)
                GetBlock(blockPosition + Vector3.Down).BlockUpdate(this, blockPosition + Vector3.Down, blockPosition);

            GetBlock(blockPosition + Vector3.Left).BlockUpdate(this, blockPosition + Vector3.Left, blockPosition);
            GetBlock(blockPosition + Vector3.Right).BlockUpdate(this, blockPosition + Vector3.Right, blockPosition);
            GetBlock(blockPosition + Vector3.Backwards).BlockUpdate(this, blockPosition + Vector3.Backwards, blockPosition);
            GetBlock(blockPosition + Vector3.Forwards).BlockUpdate(this, blockPosition + Vector3.Forwards, blockPosition);
        }

        private void DoEntityUpdates(object discarded)
        {
            for (int i = 0; i < Entities.Count; i++) // TODO: Marshall entities into chunks?
                Entities[i].PhysicsUpdate(this);
            //EntityUpdateTimer.Change(Level.TickLength, Timeout.Infinite);
        }

        public void Save()
        {
            lock (Regions)
            {
                foreach (var region in Regions)
                    region.Value.Save();
            }
        }

        public void Save(string path)
        {
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
            Directory = path;
            lock (Regions)
            {
                foreach (var region in Regions)
                    region.Value.Save(Path.Combine(Directory, Region.GetRegionFileName(region.Key)));
            }
        }

        public void ScheduleBlockUpdate(DateTime time, Vector3 position)
        {
            if (time > DateTime.Now)
            {
                lock (PendingBlockUpdatesLock)
                    PendingBlockUpdates.Add(new TimedBlockUpdate(time, position));
            }
            // TODO: Schedule updates upon loading chunks
        }

        public bool UpdatePending(Vector3 position)
        {
            lock (PendingBlockUpdatesLock)
                return PendingBlockUpdates.Any(update => update.Position == position);
        }

        public void DoScheduledUpdates()
        {
            var time = DateTime.Now;
            lock (PendingBlockUpdatesLock)
            {
                for (int i = 0; i < PendingBlockUpdates.Count; i++)
                {
                    if (PendingBlockUpdates[i].Time <= time)
                    {
                        var block = GetBlock(PendingBlockUpdates[i].Position);
                        block.OnScheduledUpdate(this, PendingBlockUpdates[i].Position);
                        PendingBlockUpdates.RemoveAt(i--);
                    }
                }
            }
        }

        private Vector3 FindBlockPosition(Vector3 position, out Chunk chunk)
        {
            var x = Math.Floor(position.X);
            var y = Math.Floor(position.Y);
            var z = Math.Floor(position.Z);

            if (y < 0 || y >= Chunk.Height)
                throw new ArgumentOutOfRangeException("position", "Position is out of range");

            var chunkX = Math.Floor(x / (Chunk.Width));
            var chunkZ = Math.Floor(z / (Chunk.Depth));

            chunk = GetChunk(new Vector3(chunkX, 0, chunkZ));
            return new Vector3((x - chunkX * Chunk.Width) % Chunk.Width, y, (z - chunkZ * Chunk.Depth) % Chunk.Depth);
        }

        private Region CreateOrLoadRegion(Vector3 position)
        {
            lock (Regions)
            {
                if (!Regions.ContainsKey(position))
                {
                    if (Directory == null)
                        Regions.Add(position, new Region(position, this));
                    else
                        Regions.Add(position,
                                new Region(position, this,
                                           Path.Combine(Directory, Region.GetRegionFileName(position))));
                }
                return Regions[position];
            }
        }

        /// <summary>
        /// Gets the coordinates of the chunk that contains the given world coordinates.
        /// </summary>
        public static Vector3 WorldToChunkCoordinates(Vector3 position)
        {
            var x = (int)position.X;
            var y = (int)position.Y;
            var z = (int)position.Z;

            if (y < 0 || y >= Chunk.Height)
                throw new ArgumentOutOfRangeException("position", "Position is out of range");

            int chunkX = x / (Chunk.Width) - ((x < 0) ? 1 : 0);
            int chunkZ = z / (Chunk.Depth) - ((z < 0) ? 1 : 0);

            return new Vector3(chunkX, 0, chunkZ);
        }

        /// <summary>
        /// Gets the position of the given block in world coordinates relative to
        /// its parent chunk.
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public static Vector3 FindBlockPosition(Vector3 position)
        {
            var x = (int)position.X;
            var y = (int)position.Y;
            var z = (int)position.Z;

            if (y < 0 || y >= Chunk.Height)
                throw new ArgumentOutOfRangeException("position", "Position is out of range");

            int chunkX = x / (Chunk.Width) - ((x < 0) ? 1 : 0);
            int chunkZ = z / (Chunk.Depth) - ((z < 0) ? 1 : 0);

            return new Vector3(x - chunkX * Chunk.Width, y, z - chunkZ * Chunk.Depth);
        }

        /// <summary>
        /// Returns the position of the specified chunk relative to its parent region.
        /// </summary>
        public static Vector3 GetRelativeChunkPosition(Vector3 position)
        {
            //In chunks
            var x = (int)position.X;
            var z = (int)position.Z;

            //In regions
            int regionX = x / Region.Width - ((x < 0) ? 1 : 0);
            int regionZ = z / Region.Depth - ((z < 0) ? 1 : 0);

            return new Vector3(x - regionX * 32, 0, z - regionZ * 32);
        }

        public static bool IsValidPosition(Vector3 position)
        {
            return position.Y >= 0 && position.Y <= 255;
        }

        protected internal virtual void OnSpawnEntity(Entity entity)
        {
            if (SpawnEntity != null)
                SpawnEntity(this, new EntityEventArgs(entity));
        }

        protected internal virtual void OnDestroyEntity(Entity entity)
        {
            if (DestroyEntity != null)
                DestroyEntity(this, new EntityEventArgs(entity));
        }

        /// <summary>
        /// Relights all loaded chunks.
        /// This method is time consuming and not reccomended for normal use.
        /// </summary>
        public void Relight()
        {
            lock (Regions)
            {
                foreach (var region in Regions)
                {
                    lock (region.Value.Chunks)
                    {
                        foreach (var chunk in region.Value.Chunks)
                            LightChunk(chunk.Value);
                    }
                }
            }
        }
    }
}