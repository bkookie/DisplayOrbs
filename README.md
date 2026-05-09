# DisplayOrbs

Quick and dirty way to display dynamic combat information (eg. stacks of a power or relic) by utilizing the Defect's orb slots.

DisplayOrbs are just visual aids, and not meant to have any combat effect. They will be pushed aside/removed to make may for actual Orbs.

Usage:

1. Create your own DisplayOrb class, inheriting from DisplayOrbModel
2. Implement IDisplayOrbGenerator\<T> on a class of your choosing
3. Call the following two methods from the above class:
	- DisplayOrbManager.Register(Owner.Player, this) - Allows for automatic refreshing of this IDisplayOrbGenerator\<T>
    - DisplayOrbManager.RefreshAllOrbs(choiceContext, Owner.Player) - Manually refresh all DisplayOrbs for this player (eg. when power amount changes)