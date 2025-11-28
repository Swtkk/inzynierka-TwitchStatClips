SELECT ChannelLogin
      
  FROM dbo.GetStats_AllTime order by MaxViewers desc


  SELECT TOP (1000) [ChannelLogin]
      ,[AvgViewers]
      ,[MaxViewers]
      ,[MinutesStreamed]
      ,[HoursWatched]
      ,[CurrentViewers]
      ,[FollowersLatest]
      ,[LastSeenAt]
      ,[CurrentLanguage]
      ,[CurrentGame]
  FROM [TwitchStats].[dbo].[GetStats_AllTime]

  GO
  CREATE OR ALTER VIEW dbo.GetStats_AllTime_WithId
AS
SELECT
    gs.*,              -- wszystkie kolumny z GetStats_AllTime
    sc.ChannelId,
    sc.FollowersTotal  -- ¿eby mo¿na by³o filtrowaæ NULL-e
FROM dbo.GetStats_AllTime gs
LEFT JOIN dbo.StreamCurrent sc
    ON sc.ChannelLogin = gs.ChannelLogin;
GO


SELECT TOP 100 ChannelLogin, MaxViewers, ChannelId, FollowersTotal
FROM dbo.GetStats_AllTime_WithId where FollowersTotal is null
ORDER BY MaxViewers DESC;


SELECT ChannelLogin,ChannelId
        FROM dbo.GetStats_AllTime_WithId WITH (NOLOCK)
        WHERE ChannelId IS NOT NULL
        ORDER BY MaxViewers DESC;