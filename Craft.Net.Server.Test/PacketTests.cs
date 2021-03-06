using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Craft.Net.Data;
using Craft.Net.Data.Generation;
using NUnit.Framework;

namespace Craft.Net.Server.Test
{
    [TestFixture]
    public class PacketTests
    {
        [Test]
        [Explicit]
        public void TestSoundEffects()
        {
            var server = new MinecraftServer(new IPEndPoint(IPAddress.Loopback, 25565));
            server.AddLevel(new Level());
            server.Settings.MotD = "Sound effect test";
            server.Settings.OnlineMode = false;
            server.Start();
            bool success = true;
            string failedSound = "n/a";
            DateTime inconclusiveTime = DateTime.Now.AddSeconds(100);

            Queue<string> effects = new Queue<string>();
            Thread test = null;

            foreach (var effect in typeof(SoundEffect).GetFields().Where(f => f.FieldType == typeof(string) && f.IsLiteral))
            {
                effects.Enqueue(effect.GetValue(new SoundEffect()) as string);
            }

            server.PlayerLoggedIn += (s ,e) =>
                {
                    e.Client.SendChat("Beginning sound effect test in 5 seconds. Type \"fail\" into chat to indicate failure.");
                    inconclusiveTime = DateTime.MaxValue;
                    test = new Thread(new ThreadStart(() =>
                        {
                            Thread.Sleep(5000);
                            while (effects.Any())
                            {
                                e.Client.SendChat("Playing sound: " + effects.Peek());
                                e.Client.SendPacket(new NamedSoundEffectPacket(effects.Peek(), (int)e.Client.Entity.Position.X,
                                    (int)e.Client.Entity.Position.Y, (int)e.Client.Entity.Position.Z, 1.0f, 63));
                                Thread.Sleep(5000);
                                effects.Dequeue();
                            }
                        }));
                    test.Start();
                    e.Handled = true;
                };

            server.PlayerLoggedOut += (s, e) =>
                {
                    test.Abort();
                    server.Stop();
                    success = false;
                    failedSound = "Player left before test completion.";
                    effects = new Queue<string>();
                    e.Handled = true;
                    Assert.Fail("Player left before test completion.");
                };

            server.ChatMessage += (s, e) =>
                {
                    if (e.RawMessage == "fail")
                    {
                        test.Abort();
                        server.Stop();
                        failedSound = effects.Peek();
                        effects = new Queue<string>();
                        success = false;
                        Assert.Fail("Sound effect: " + effects.Peek());
                    }
                };

            while (effects.Count != 0 && DateTime.Now < inconclusiveTime) { Thread.Sleep(100); }
            if (DateTime.Now >= inconclusiveTime)
                Assert.Inconclusive("No player joined within 10 second time limit.");
            else
            {
                if (success)
                    Assert.Pass();
                else
                    Assert.Fail("Failed sound effect: " + failedSound);
            }
        }

        [Test]
        [Explicit]
        public void TestParticleEffects()
        {
            var server = new MinecraftServer(new IPEndPoint(IPAddress.Loopback, 25565));
            server.AddLevel(new Level(new FlatlandGenerator()));
            server.DefaultLevel.SpawnPoint = new Vector3(0, 4, -2);
            server.Settings.MotD = "Particle effect test";
            server.Settings.OnlineMode = false;
            server.Start();
            bool success = true;
            string failedSound = "n/a";
            DateTime inconclusiveTime = DateTime.Now.AddSeconds(100);

            var effects = new Queue<string>();
            Thread test = null;

            foreach (var effect in typeof(ParticleEffect).GetFields().Where(f => f.FieldType == typeof(string) && f.IsLiteral))
            {
                effects.Enqueue(effect.GetValue(new SoundEffect()) as string);
            }

            server.PlayerLoggedIn += (s, e) =>
            {
                e.Client.SendChat("Beginning particle effect test in 5 seconds. Type \"fail\" into chat to indicate failure.");
                inconclusiveTime = DateTime.MaxValue;
                test = new Thread(() =>
                    {
                        Thread.Sleep(5000);
                        while (effects.Any())
                        {
                            e.Client.SendChat("Spawning particles: " + effects.Peek());
                            e.Client.SendPacket(new ParticleEffectPacket(effects.Peek(), 0, 4, 0, 0, 0, 0, 0.25f, 10));
                            Thread.Sleep(5000);
                            effects.Dequeue();
                        }
                    });
                test.Start();
                e.Handled = true;
            };

            server.PlayerLoggedOut += (s, e) =>
            {
                test.Abort();
                server.Stop();
                success = false;
                failedSound = "Player left before test completion.";
                effects = new Queue<string>();
                e.Handled = true;
                Assert.Fail("Player left before test completion.");
            };

            server.ChatMessage += (s, e) =>
            {
                if (e.RawMessage == "fail")
                {
                    test.Abort();
                    server.Stop();
                    failedSound = effects.Peek();
                    effects = new Queue<string>();
                    success = false;
                    Assert.Fail("Particle effect: " + effects.Peek());
                }
            };

            while (effects.Count != 0 && DateTime.Now < inconclusiveTime) { Thread.Sleep(100); }
            if (DateTime.Now >= inconclusiveTime)
                Assert.Inconclusive("No player joined within 10 second time limit.");
            else
            {
                if (success)
                    Assert.Pass();
                else
                    Assert.Fail("Failed particle effect: " + failedSound);
            }
        }
    }
}
