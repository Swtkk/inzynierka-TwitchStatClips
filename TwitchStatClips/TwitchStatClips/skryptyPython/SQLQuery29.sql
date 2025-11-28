SELECT TOP (1000) [ChannelLogin]
      ,[FollowersMax]
      ,[FollowersMin]
      ,[FollowersGained]
      ,[FirstBucket]
      ,[LastBucket]
      ,[FollowersTotalNow]
  FROM [TwitchStats].[dbo].[GetFollowers_AllTime]  order by FollowersTotalNow desc
