using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SuperPutty.Utils;

namespace SuperPuttyUnitTests
{
    /// <summary>
    /// The built-in highlighting rule sets turned off (OutputHighlighting.MergeRuleSets, Offered), as SuperXTerminal's
    /// a701db1: hidden but still there for a set that includes one, and replaced by a set of the user's with their id
    /// </summary>
    [TestFixture]
    public class OutputHighlightingTests
    {
        /// <summary>A rule set with an id</summary>
        /// <param name="id">Its id</param>
        /// <returns>The set</returns>
        private static JObject Set(string id)
        {
            return new JObject { ["id"] = id, ["name"] = id, ["rules"] = new JArray() };
        }

        /// <summary>On, every set is offered, the built-in ones first</summary>
        [Test]
        public void BuiltInOnOffersAll()
        {
            JArray sets = OutputHighlighting.MergeRuleSets(new[] { Set("generic"), Set("cisco") }, new[] { new[] { Set("mine") } }, true);
            CollectionAssert.AreEqual(new[] { "generic", "cisco", "mine" }, OutputHighlighting.Offered(sets).Select(s => (string)s["id"]).ToArray());
        }

        /// <summary>Off, the built-in sets are still sent (for includes) but hidden, and not offered</summary>
        [Test]
        public void BuiltInOffHidesThem()
        {
            JArray sets = OutputHighlighting.MergeRuleSets(new[] { Set("generic"), Set("cisco") }, new[] { new[] { Set("mine") } }, false);
            Assert.AreEqual(3, sets.Count);
            Assert.IsTrue((bool)sets[0]["hidden"]);
            CollectionAssert.AreEqual(new[] { "mine" }, OutputHighlighting.Offered(sets).Select(s => (string)s["id"]).ToArray());
        }

        /// <summary>Off, a set of the user's with a built-in's id replaces it and is theirs, so it's offered</summary>
        [Test]
        public void UsersSetWithBuiltInIdIsTheirs()
        {
            JObject theirs = Set("CISCO");
            theirs["name"] = "Our Cisco";
            JArray sets = OutputHighlighting.MergeRuleSets(new[] { Set("generic"), Set("cisco") }, new[] { new JObject[0], new[] { theirs } }, false);
            CollectionAssert.AreEqual(new[] { "Our Cisco" }, OutputHighlighting.Offered(sets).Select(s => (string)s["name"]).ToArray());
        }
    }
}
