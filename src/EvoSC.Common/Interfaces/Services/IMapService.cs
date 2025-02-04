﻿using System.Data;
using EvoSC.Common.Database.Models.Maps;
using EvoSC.Common.Interfaces.Models;
using EvoSC.Common.Models;
using EvoSC.Common.Models.Maps;

namespace EvoSC.Common.Interfaces.Services;

public interface IMapService
{
    /// <summary>
    /// Gets a map.
    /// </summary>
    /// <param name="id">The database ID.</param>
    /// <returns></returns>
    public Task<IMap?> GetMapById(long id);
    
    /// <summary>
    /// Gets a map.
    /// </summary>
    /// <param name="uid">The maps unique identifier.</param>
    /// <returns></returns>
    public Task<IMap?> GetMapByUid(string uid);
    
    /// <summary>
    /// Adds a map to the server. If the map already exists and the passed map is newer than the
    /// existing one, the existing one will be overwritten.
    /// </summary>
    /// <param name="mapStream">An object containing the map file and the map metadata.</param>
    /// <exception cref="DuplicateNameException">Thrown if the map already exists within the database.</exception>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task<IMap> AddMap(MapStream mapStream);
    
    /// <summary>
    /// Add several maps to the server. Useful for adding mappacks.
    /// </summary>
    /// <param name="mapStreams">A list of objects containing the mapfile and the map metadata.</param>
    /// <returns></returns>
    public Task<IEnumerable<IMap>> AddMaps(List<MapStream> mapStreams);
    
    /// <summary>
    /// Removes a map from the server.
    /// </summary>
    /// <param name="mapId">The maps ID in the database.</param>
    /// <returns></returns>
    public Task RemoveMap(long mapId);
}
