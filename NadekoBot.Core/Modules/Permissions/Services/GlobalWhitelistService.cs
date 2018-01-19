using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using System.Collections.Generic;
using NadekoBot.Common.Collections;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Core.Services;
using NadekoBot.Core.Services.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Modules.Permissions.Services
{
    public class GlobalWhitelistService : INService
    {
        private readonly DbService _db;
        public ConcurrentHashSet<ulong> GlobalWhitelistedUsers { get; }
        public ConcurrentHashSet<ulong> GlobalWhitelistedGuilds { get; }
        public ConcurrentHashSet<ulong> GlobalWhitelistedChannels { get; }

        public GlobalWhitelistService(IBotConfigProvider bc, DbService db)
        {
            _db = db;

            var GlobalWhitelist = bc.BotConfig.GlobalWhitelistMembers;
            GlobalWhitelistedUsers = new ConcurrentHashSet<ulong>(GlobalWhitelist.Where(bi => bi.Type == GlobalWhitelistType.User).Select(c => c.ItemId));
            GlobalWhitelistedGuilds = new ConcurrentHashSet<ulong>(GlobalWhitelist.Where(bi => bi.Type == GlobalWhitelistType.Server).Select(c => c.ItemId));
            GlobalWhitelistedChannels = new ConcurrentHashSet<ulong>(GlobalWhitelist.Where(bi => bi.Type == GlobalWhitelistType.Channel).Select(c => c.ItemId));
        }

        public bool CreateWhitelist(string name)
        {
            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate(set => set
                    .Include(x => x.GlobalWhitelistGroups));

                var group = new GlobalWhitelistSet()
                {
                    ListName = name
                };
                bc.GlobalWhitelistGroups.Add(group);

                uow.Complete();
            }

            return true;
        }
        public bool DeleteWhitelist(string name)
        {
            using (var uow = _db.UnitOfWork)
            {
                // NOTE: using bc to remove will only set the BotConfigId FK to null

                // Delete the whitelist record and all relation records
                uow._context.Set<GlobalWhitelistSet>().Remove( 
                    uow._context.Set<GlobalWhitelistSet>()
                    .Where( x => x.ListName == name ).FirstOrDefault()
                );
                uow.Complete();
            }
            return true;
        }
        public string[] GetAllNames(int page)
        {
            var names = new List<string>();

            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate();
                uow._context.SaveChanges();

                // First, Collect all lists
                var groups = uow.GlobalWhitelists.GetWhitelistGroups(page);
                
                // Then, Take just the names
                for ( var i=0; i<groups.Length; i++ ) 
                {
                    names.Add(groups[i].ListName);
                }

                uow.Complete();
            }
            return names.ToArray();
        }

        public string[] GetNamesByMember(ulong id, GlobalWhitelistType type, int page)
        {
            var names = new List<string>();

            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate();
                uow._context.SaveChanges();

                // First, Retrieve the WhitelistItem given id,type
                var item = bc.GlobalWhitelistMembers
                        .Where( x => x.ItemId.Equals(id) )
                        .Where( x => x.Type.Equals(type) )
                        .FirstOrDefault();

                if (item == null) return names.ToArray();

                // Second, Collect all lists related to item
                var groups = uow.GlobalWhitelists.GetWhitelistGroupsByMember(item.Id,page);

                // Third, Take just the names
                for ( var i=0; i<groups.Length; i++ ) 
                {
                    names.Add(groups[i].ListName);
                }

                uow.Complete();
            }
            return names.ToArray();
        }

        public string[] GetNamesByUnblocked(string name, string type)
        {
            string[] names = null;
            bool exists;
            UnblockedCmdOrMdl item;
            // Get the item
            if (type == "command") {
                exists = GetUbCmdByName(name, out item);
            } else {
                exists = GetUbMdlByName(name, out item);
            }

            if (exists) {
                using (var uow = _db.UnitOfWork)
                {
                    var bc = uow.BotConfig.GetOrCreate(set => set
                        .Include(x => x.GlobalWhitelistGroups)
                        .Include(x => x.UnblockedModules)
                          .ThenInclude(x => x.GlobalUnblockedSets)
                        .Include(x => x.UnblockedCommands)
                          .ThenInclude(x => x.GlobalUnblockedSets));
                    
                    names = uow._context.Set<GlobalUnblockedSet>()
                    .Where( x => x.UnblockedPK == item.Id )
                    .Join(
                        bc.GlobalWhitelistGroups, 
                        u => u.ListPK, g => g.Id,
                        (u,g) => new {
                            g.ListName
                        })
                    .Select(g => g.ListName)
                    .ToArray();

                    uow.Complete();
                }
            }            
            return names;
        }

        public GlobalWhitelistSet[] GetGroupsByUnblocked(string name, string type)
        {
            GlobalWhitelistSet[] groups = null;
            bool exists;
            UnblockedCmdOrMdl item;
            // Get the item
            if (type == "command") {
                exists = GetUbCmdByName(name, out item);
            } else {
                exists = GetUbMdlByName(name, out item);
            }

            if (exists) {
                using (var uow = _db.UnitOfWork)
                {
                    var bc = uow.BotConfig.GetOrCreate(set => set
                        .Include(x => x.GlobalWhitelistGroups)
                        .Include(x => x.UnblockedModules)
                          .ThenInclude(x => x.GlobalUnblockedSets)
                        .Include(x => x.UnblockedCommands)
                          .ThenInclude(x => x.GlobalUnblockedSets));
                    
                    groups = uow._context.Set<GlobalUnblockedSet>()
                    .Where( x => x.UnblockedPK == item.Id )
                    .Join(
                        bc.GlobalWhitelistGroups, 
                        u => u.ListPK, g => g.Id,
                        (u,g) => g)
                    .ToArray();

                    uow.Complete();
                }
            }            
            return groups;
        }

        public bool GetGroupByName(string listName, out GlobalWhitelistSet group)
        {
            group = null;

            if (string.IsNullOrWhiteSpace(listName)) return false;

            using (var uow = _db.UnitOfWork)
            {
                group = uow.GlobalWhitelists.GetByName(listName);

                if (group == null) { return false; }
                else { return true; }
            }
        }
        public ulong[] GetGroupMembers(GlobalWhitelistSet group, GlobalWhitelistType type, int page)
        {
            ulong[] results = null;

            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate();
                uow._context.SaveChanges();

                results = group.GlobalWhitelistItemSets
                  .Join(bc.GlobalWhitelistMembers.Where(i => i.Type == type), 
                      p => p.ItemPK, 
                      m => m.Id, 
                      (pair,member) => member.ItemId)
                  .ToArray();

                uow.Complete();
            }

            //System.Console.WriteLine("Type {0} Results: {1}", type, results.Length);

            return results;
        }

        public bool IsMemberInGroup(ulong id, GlobalWhitelistSet group)
        {
            var result = true;

            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate(set => set
                    .Include(x => x.GlobalWhitelistMembers));
                
                var temp = bc.GlobalWhitelistMembers
                .Where( x => x.ItemId == id ) // GWLItem.Id for which id is the GWLItem.ItemId
                .Join(
                    uow._context.Set<GlobalWhitelistItemSet>(), 
                    m => m.Id, i => i.ItemPK,
                    (m,i) => new {
                        m.ItemId,
                        i.ListPK
                    })
                .Where( y => y.ListPK == group.Id ) // Temporary table for which list matches group
                .FirstOrDefault();

                uow.Complete();
                
                if (temp != null) {
                  //System.Console.WriteLine("Item {0}, List {0}", temp.ItemId,temp.ListPK);
                  result = true;
                } else {
                  //System.Console.WriteLine("Null!");
                  result = false;
                }
            }
            return result;
        }

        public bool AddItemToGroup(ulong id, GlobalWhitelistType type, GlobalWhitelistSet group)
        {
            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate();
                uow._context.SaveChanges();

                // First GetOrCreate the WhitelistItem given id and type
                var item = bc.GlobalWhitelistMembers
                        .Where( x => x.ItemId.Equals(id) )
                        .Where( x => x.Type.Equals(type) )
                        .FirstOrDefault();
                
                if (item == null) {
                    item = new GlobalWhitelistItem
                    {
                        ItemId = id,
                        Type = type
                    };
                    bc.GlobalWhitelistMembers.Add(item);
                    //uow._context.Set<GlobalWhitelistItem>().Add(item); // don't do this as it sets BotConfigId to null!
                    uow._context.SaveChanges();
                    uow._context.Set<GlobalWhitelistItemSet>().Add(new GlobalWhitelistItemSet
                    {
                        ListPK = group.Id,
                        ItemPK = item.Id
                    });
                } else {
                    // Second Check that id is not already in the group
                    var itemInGroup = group.GlobalWhitelistItemSets
                        .FirstOrDefault(x => x.ItemPK == item.Id);

                    if (itemInGroup != null) return false; // already exists!
                    
                    // Third Create a new WhitelistItemSet
                    itemInGroup = new GlobalWhitelistItemSet
                    {
                        ListPK = group.Id,
                        ItemPK = item.Id
                    };

                    // Finally add the WhitelistItemSet
                    uow._context.Set<GlobalWhitelistItemSet>().Add(itemInGroup);
                }
                uow.Complete();
            }
            return true;
        }
        public bool RemoveItemFromGroup(ulong id, GlobalWhitelistType type, GlobalWhitelistSet group)
        {
            using (var uow = _db.UnitOfWork)
            {
                bool exists;
                GlobalWhitelistItem item;

                // Get the item
                exists = GetMemberByIdType(id, type, out item);

                System.Console.WriteLine("Item id {0}, type {1}, dId {2}", item.Id, item.Type, item.ItemId );
                System.Console.WriteLine("Group id {0}, name {1}", group.Id, group.ListName);

                if (item == null) return false;

                // Second, Find the ItemInSet record
                var itemInSet = group.GlobalWhitelistItemSets
                    .FirstOrDefault(x => x.ItemPK == item.Id);

                // Finally remove the Item from the Set
                uow._context.Set<GlobalWhitelistItemSet>().Remove(itemInSet);

                uow.Complete();
            }
            return true;
        }

        public bool GetMemberByIdType(ulong id, GlobalWhitelistType type, out GlobalWhitelistItem item)
        {
            item = null;

            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate(set => set
                    .Include(x => x.GlobalWhitelistMembers));

                // Retrieve the member item given name
                item = bc.GlobalWhitelistMembers
                    .Where( x => x.Type.Equals(type) )
                    .Where( x => x.ItemId.Equals(id) )
                    .FirstOrDefault();

                if (item == null) { return false; }
                else { return true; }
            }
        }

        public bool AddItemToGroup(string name, string type, GlobalWhitelistSet group)
        {   
            GlobalUnblockedSet itemInGroup;

            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate(set => set
                    .Include(x => x.UnblockedModules)
                      .ThenInclude(x => x.GlobalUnblockedSets)
                    .Include(x => x.UnblockedCommands)
                      .ThenInclude(x => x.GlobalUnblockedSets));

                bool exists;
                UnblockedCmdOrMdl item;

                // Get the item
                if (type == "command") {
                    exists = GetUbCmdByName(name, out item);
                } else {
                    exists = GetUbMdlByName(name, out item);
                }

                // Check if item already exists
                if (!exists) {
                    // Add new item to DB
                    item = new UnblockedCmdOrMdl
                    {
                        Name = name,
                    };                        
                    if (type == "command") {
                      bc.UnblockedCommands.Add(item);
                    } else {
                      bc.UnblockedModules.Add(item);
                    }
                    uow.Complete();
                }

                // Check if relationship already exists
                itemInGroup = group.GlobalUnblockedSets
                    .FirstOrDefault(x => x.UnblockedPK == item.Id);

                if (itemInGroup != null) return false; // already exists!
                
                // Add relationship to DB
                itemInGroup = new GlobalUnblockedSet
                {
                    ListPK = group.Id,
                    UnblockedPK = item.Id
                };
                uow._context.Set<GlobalUnblockedSet>().Add(itemInGroup);
                uow.Complete();
            }
            return true;
        }
        public bool RemoveItemFromGroup(string name, string type, GlobalWhitelistSet group)
        {
            using (var uow = _db.UnitOfWork)
            {
                bool exists;
                UnblockedCmdOrMdl item;

                // Get the item
                if (type == "command") {
                    exists = GetUbCmdByName(name, out item);
                } else {
                    exists = GetUbMdlByName(name, out item);
                }

                if (item == null) return false;

                // Second, Find the relationship record
                var itemInSet = group.GlobalUnblockedSets
                    .FirstOrDefault(x => x.UnblockedPK == item.Id);

                // Finally remove the Item from the Set
                uow._context.Set<GlobalUnblockedSet>().Remove(itemInSet);

                uow.Complete();
            }
            return true;
        }

        public bool GetUbCmdByName(string name, out UnblockedCmdOrMdl item)
        {
            item = null;

            if (string.IsNullOrWhiteSpace(name)) return false;

            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate(set => set
                    .Include(x => x.UnblockedCommands));

                // Retrieve the UnblockedCmdOrMdl item given name
                item = bc.UnblockedCommands
                    .Where( x => x.Name.Equals(name) )
                    .FirstOrDefault();

				uow.Complete();

                if (item == null) { return false; }
                else { return true; }
            }
        }

        public bool GetUbMdlByName(string name, out UnblockedCmdOrMdl item)
        {
            item = null;

            if (string.IsNullOrWhiteSpace(name)) return false;

            using (var uow = _db.UnitOfWork)
            {
                var bc = uow.BotConfig.GetOrCreate(set => set
                    .Include(x => x.UnblockedModules));

                // Retrieve the UnblockedCmdOrMdl item given name
                item = bc.UnblockedModules
                    .Where( x => x.Name.Equals(name) )
                    .FirstOrDefault();

				uow.Complete();

                if (item == null) { return false; }
                else { return true; }
            }
        }

		public UnblockedCmdOrMdl[] GetGroupUnblocked(GlobalWhitelistSet group)
		{
			UnblockedCmdOrMdl[] items;
			using (var uow = _db.UnitOfWork)
            {
                // Retrieve a list of unblocked names linked to group on GlobalUnblockedSets.UnblockedPK
                items = group.GlobalUnblockedSets
					.Join(
						uow._context.Set<UnblockedCmdOrMdl>(), 
						gu => gu.UnblockedPK, u => u.Id,
						(relation, unblocked) => unblocked
						)
					.ToArray();
                uow.Complete();
            }
			return items;
		}

		public string[] GetGroupUnblockedNames(GlobalWhitelistSet group, UnblockedType type)
		{
			string[] names;
			using (var uow = _db.UnitOfWork)
            {
                // Retrieve a list of unblocked names linked to group on GlobalUnblockedSets.UnblockedPK
                names = group.GlobalUnblockedSets
					.Join(
						uow._context.Set<UnblockedCmdOrMdl>().Where(u => u.Type.Equals(type)), 
						gu => gu.UnblockedPK, u => u.Id,
						(relation, unblocked) => unblocked.Name
						)
					.ToArray();
                uow.Complete();
            }
			return names;
		}
    }
}
